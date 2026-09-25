#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Xml.Serialization;

using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;

using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;

// IMPORTANT: alias WPF brushes to avoid conflict with SharpDX.Direct2D1.Brush
using WpfBrush = System.Windows.Media.Brush;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfColors = System.Windows.Media.Colors;
using WpfBrushes = System.Windows.Media.Brushes;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public enum ProfileModesOpt
	{
		LegToLeg = 0,
		SessionCurrentDay = 1
	}

	public enum VolumeProfileLayerOpt
	{
		BehindDelta = 0,
		InFrontOfDelta = 1,
		LeftOfDelta = 2,
		RightOfDelta = 3
	}

	public enum LegRotationModeOpt
	{
		DistanceFromExtreme = 0,
		TrueSwingAlternate = 1
	}

	public enum SessionHoursOpt
	{
		Chart = 0,
		Eth = 1,
		Rth = 2
	}

	internal enum LegDirectionOpt
	{
		Unknown = 0,
		Up = 1,
		Down = -1
	}

	public class LegToLegDeltaProfile_Opt : Indicator
	{
		// Per primary bar maps
		private List<Dictionary<double, long>> barDeltaMaps;
		private List<Dictionary<double, long>> barVolMaps;

		// Active (current) maps
		private readonly Dictionary<double, long> legDeltaByPrice = new Dictionary<double, long>();
		private readonly Dictionary<double, long> sessionDeltaByPrice = new Dictionary<double, long>();

		private readonly Dictionary<double, long> legVolByPrice = new Dictionary<double, long>();
		private readonly Dictionary<double, long> sessionVolByPrice = new Dictionary<double, long>();

		// Start bar index for current session (primary series)
		private int sessionStartBar = -1;

		// Start bar index for current "leg profile" (primary series)
		private int legStartBar = -1;

		// Rotation tracking (for LegToLeg mode)
		private double legHigh = double.NaN;
		private double legLow  = double.NaN;
		private int    legHighBar = -1;
		private int    legLowBar  = -1;
		private LegDirectionOpt legDir = LegDirectionOpt.Unknown;

	// Rebuilds differes en historique (perf chargement) : la state machine tourne
	// sur chaque barre mais les re-sommages (jusqu'a 2000 barres) sont
	// consolides a la fin du chargement. En temps reel, flush immediat.
	// Etat final identique aux rebuilds immediats de l'original.
	private bool _needLegRebuild = false;
		private bool _needSessionRebuild = false;
		private bool _progStarted = false;
		private long _tickCount = 0;
		private long _tickNext = 500000;
		private int _progBars = 0;
		private int _renderErrCount = 0;
		private System.Diagnostics.Stopwatch _swTotal = new System.Diagnostics.Stopwatch();

		// P0-opt thread-safety : OnRender (SharpDX) tourne sur un thread parallele
		// aux ticks OnEachTick. Toute mutation de map + toute copie rendu passent
		// par ce verrou (copie courte sous verrou, rendu hors verrou).
		private readonly object _sync = new object();

	// Session ETH/RTH : occurrences hebdomadaires extraites du template NT8
	// de l'instrument (TradingHours.GetEthRth). Resolution week-aware tenant
	// compte de BeginDay ET EndDay, en heure locale des barres (pas de
	// conversion de fuseau : horaires NT8 et Time[] sont exchange-local).
	// Chart = pas de schedule (bornes = Bars.IsFirstBarOfSession).
	private struct SessionWindowOpt
	{
		public readonly DayOfWeek BeginDay;
		public readonly TimeSpan BeginTime;
		public readonly DayOfWeek EndDay;
		public readonly TimeSpan EndTime;
		public SessionWindowOpt(DayOfWeek bDay, TimeSpan bTime, DayOfWeek eDay, TimeSpan eTime)
		{ BeginDay = bDay; BeginTime = bTime; EndDay = eDay; EndTime = eTime; }
	}
	private SessionHoursOpt _schedFor = (SessionHoursOpt)(-1);
	private List<SessionWindowOpt> _schedWindows;
	// Derniere valeur appliquee de SessionHours : NT8 ne rejoue pas DataLoaded
	// sur changement de parametre au dialogue -> reset manuel dans OnBarUpdate.
	private SessionHoursOpt _appliedSessionHours = (SessionHoursOpt)(-1);

		// Cache largeurs de texte (evite un TextLayout par ligne et par frame).
		private readonly Dictionary<string, float> _textWidthCache = new Dictionary<string, float>(256);
		private readonly Dictionary<int, TextFormat> _fontCache = new Dictionary<int, TextFormat>();

		// Bid/Ask cache (Tick Replay)
		private double lastBid = double.NaN;
		private double lastAsk = double.NaN;

		// Tick-rule fallback
		private double prevLast = double.NaN;

		// Rendering resources
		private TextFormat textFormat;
		private SolidColorBrush posBrushDx, negBrushDx, textBrushDx, spineBrushDx, volBrushDx, borderBrushDx;

		// Brush change detection so settings update without requiring render-target changes
		private string lastPosBrushSer, lastNegBrushSer, lastTextBrushSer, lastVolBrushSer, lastBorderBrushSer;
		private float  lastDeltaOpacity = -1f;
		private float  lastVolOpacity = -1f;
		private int    lastFontSize = -1;

		// P0-opt : label chart epure (nom seul, sans la liste des ~27 parametres).
		public override string DisplayName { get { return Name; } }

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name        = "LegToLegDeltaProfile Opt";
				Description = "Rotation-based leg delta profile OR current-session delta profile, with optional tick-based volume profile layer (behind/infront/side-by-side).";
		Calculate   = Calculate.OnEachTick;
		IsOverlay   = true;

		ProfileMode = ProfileModesOpt.LegToLeg;
		SessionHours = SessionHoursOpt.Chart;

				// Rotation threshold in POINTS (price units). Example NQ: 65
				RotationPoints = 65;
				RotationMode = LegRotationModeOpt.TrueSwingAlternate;

				// Delta rendering
				MaxProfileWidthPx      = 70;
				MinAbsDeltaToShow      = 1;
		RebuildLookbackBarsCap = 2000;
		ShowProgressPrints = false;

				// Auto text behavior (recommended ON) =====
				AutoDeltaText = true;
				AutoFontMin = 6;
				AutoFontMax = 18;

				// If you disable AutoDeltaText, these are used:
				FontSize = 10;

				// Auto scaling to avoid overlap when zoomed out (DELTA ONLY)
				MinRowHeightPx = 10;
				GroupByZoom = true;
				TicksPerLevel = 1;
				AdaptiveDeltaText = false;

				ShowSpine    = false;
				SpineWidthPx = 2;
				RightMarginPx = 0;

				DeltaOpacity = 0.85f;
				ShowDeltaText = true;
				ShowDeltaBorder = true;
				DeltaBorderWidthPx = 1;
				DeltaBorderBrush = WpfBrushes.Black;

				RightReservedPercent = 0.10;
				XOffsetPx = 0;

				// NT color pickers (Brushes) - DELTA
				PositiveBrush = WpfBrushes.Blue;
				NegativeBrush = WpfBrushes.Red;
				TextBrush     = WpfBrushes.LightGray;

				// Optional VOLUME profile
				ShowVolumeProfile    = true;
				VolumeLayer          = VolumeProfileLayerOpt.RightOfDelta;
				VolumeProfileWidthPx = 70;
				VolumeOpacity        = 0.25f;
				VolumeBrush          = WpfBrushes.Gray;

				SideBySideGapPx = 4;
			}
			else if (State == State.Configure)
			{
				AddDataSeries(BarsPeriodType.Tick, 1);
			}
			else if (State == State.DataLoaded)
			{
		barDeltaMaps = new List<Dictionary<double, long>>(4096);
		barVolMaps   = new List<Dictionary<double, long>>(4096);

				legDeltaByPrice.Clear();
				sessionDeltaByPrice.Clear();
				legVolByPrice.Clear();
				sessionVolByPrice.Clear();

				sessionStartBar = -1;
				legStartBar = -1;

				legHigh = double.NaN;
				legLow  = double.NaN;
				legHighBar = -1;
				legLowBar = -1;
				legDir  = LegDirectionOpt.Unknown;

				_needLegRebuild = false;
				_needSessionRebuild = false;
			_progStarted = false;
			_tickCount = 0;
			_tickNext = 500000;
			_schedFor = (SessionHoursOpt)(-1);
			_schedWindows = null;
			_appliedSessionHours = SessionHours;

				lastPosBrushSer = lastNegBrushSer = lastTextBrushSer = lastVolBrushSer = lastBorderBrushSer = null;
				lastDeltaOpacity = -1f;
				lastVolOpacity = -1f;
		lastFontSize = -1;
		_textWidthCache.Clear();
				prevLast = double.NaN;
				lastBid = double.NaN;
				lastAsk = double.NaN;
			}
			else if (State == State.Terminated)
			{
				DisposeDx();
			}
		}

		private void DisposeDx()
		{
			try
			{
				textFormat?.Dispose();
				posBrushDx?.Dispose();
				negBrushDx?.Dispose();
				textBrushDx?.Dispose();
				spineBrushDx?.Dispose();
				volBrushDx?.Dispose();
				borderBrushDx?.Dispose();
				if (_fontCache != null)
				{
					foreach (var kv in _fontCache)
					{
						try { kv.Value?.Dispose(); } catch { }
					}
					_fontCache.Clear();
				}
				_textWidthCache.Clear();
			}
			catch { }
			finally
			{
				textFormat = null;
				posBrushDx = null;
				negBrushDx = null;
				textBrushDx = null;
				spineBrushDx = null;
				volBrushDx = null;
				borderBrushDx = null;
			}
		}

		protected override void OnMarketData(MarketDataEventArgs e)
		{
			if (e.MarketDataType == MarketDataType.Bid)
				lastBid = e.Price;
			else if (e.MarketDataType == MarketDataType.Ask)
				lastAsk = e.Price;
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress == 1)
			{
				ProcessTickIntoPrimaryBar();
				return;
			}

			if (CurrentBar < 1)
				return;

			EnsureBarMaps(CurrentBar);

		// Changement de SessionHours a chaud : NT8 ne rejoue pas DataLoaded,
		// on reset l'etat complet pour ne pas contaminer la nouvelle session
		// avec les cumuls de l'ancien decoupage.
		if (SessionHours != _appliedSessionHours)
		{
			_appliedSessionHours = SessionHours;
			_schedFor = (SessionHoursOpt)(-1);
			_schedWindows = null;
			if (barDeltaMaps != null) barDeltaMaps.Clear();
			if (barVolMaps != null) barVolMaps.Clear();
			legDeltaByPrice.Clear();
			sessionDeltaByPrice.Clear();
			legVolByPrice.Clear();
			sessionVolByPrice.Clear();
			sessionStartBar = -1;
			legStartBar = -1;
			legHigh = double.NaN;
			legLow  = double.NaN;
			legHighBar = -1;
			legLowBar  = -1;
			legDir  = LegDirectionOpt.Unknown;
			prevLast = double.NaN;
		}

		if (IsFinalHistBar()) FlushPendingRebuilds();
		if (ShowProgressPrints && _progStarted && IsFinalHistBar())
		{
			_progStarted = false;
			_swTotal.Stop();
			try { Print(string.Format("[LEG] chargement termine en {0:F1}s ({1} ticks traites, primaires={2})", _swTotal.Elapsed.TotalSeconds, _tickCount, BarsArray[0].Count)); } catch {}
		}

		// Bornes de session : template du chart (Chart, = original) ou templates
		// NT8 de l'instrument (Eth/Rth via TradingHours). Les legs redemarrent
		// a chaque borne, comme l'original.
		if (IsNewSessionBar())
		{
			sessionStartBar = CurrentBar;
			_needSessionRebuild = true;

			legStartBar = sessionStartBar;
			_needLegRebuild = true;
			if (IsFinalHistBar()) FlushPendingRebuilds();

			legHigh = High[0];
				legLow  = Low[0];
				legHighBar = CurrentBar;
				legLowBar  = CurrentBar;
				legDir  = LegDirectionOpt.Unknown;
			}

			if (sessionStartBar < 0)
			{
			sessionStartBar = 0;
			_needSessionRebuild = true;
			if (IsFinalHistBar()) FlushPendingRebuilds();
			}

			if (legStartBar < 0)
			{
			legStartBar = sessionStartBar >= 0 ? sessionStartBar : 0;
			_needLegRebuild = true;
			if (IsFinalHistBar()) FlushPendingRebuilds();

				legHigh = High[0];
				legLow  = Low[0];
				legHighBar = CurrentBar;
				legLowBar  = CurrentBar;
				legDir  = LegDirectionOpt.Unknown;
			}

			if (ProfileMode == ProfileModesOpt.LegToLeg)
			{
				// Update extremes of current leg + remember where they occurred
				if (double.IsNaN(legHigh) || double.IsNaN(legLow))
				{
					legHigh = High[0];
					legLow  = Low[0];
					legHighBar = CurrentBar;
					legLowBar  = CurrentBar;
				}
				else
				{
					if (High[0] >= legHigh)
					{
						legHigh = High[0];
						legHighBar = CurrentBar;
					}
					if (Low[0] <= legLow)
					{
						legLow = Low[0];
						legLowBar = CurrentBar;
					}
				}

				if (RotationPoints > 0)
				{
					double last = Close[0];

					if (RotationMode == LegRotationModeOpt.DistanceFromExtreme)
					{
						bool rotateDown = last <= (legHigh - RotationPoints);
						bool rotateUp   = last >= (legLow  + RotationPoints);

						if (rotateDown || rotateUp)
							StartNewLegAtCurrentBar(rotateUp ? LegDirectionOpt.Up : LegDirectionOpt.Down);
					}
					else // TrueSwingAlternate (start new leg at extreme bar)
					{
						if (legDir == LegDirectionOpt.Unknown)
						{
							if (last >= (legLow + RotationPoints))
								legDir = LegDirectionOpt.Up;
							else if (last <= (legHigh - RotationPoints))
								legDir = LegDirectionOpt.Down;
						}

						if (legDir == LegDirectionOpt.Up)
						{
							if (last <= (legHigh - RotationPoints))
								StartNewLegAtBar(legHighBar, LegDirectionOpt.Down, last);
						}
						else if (legDir == LegDirectionOpt.Down)
						{
							if (last >= (legLow + RotationPoints))
								StartNewLegAtBar(legLowBar, LegDirectionOpt.Up, last);
						}
					}
				}
			}
		}

		private void StartNewLegAtCurrentBar(LegDirectionOpt newDir)
		{
			legStartBar = CurrentBar;

			legHigh = High[0];
			legLow  = Low[0];
			legHighBar = CurrentBar;
			legLowBar  = CurrentBar;

		legDir = (RotationMode == LegRotationModeOpt.TrueSwingAlternate) ? newDir : LegDirectionOpt.Unknown;

		_needLegRebuild = true;
		if (IsFinalHistBar()) FlushPendingRebuilds();
		}

		private void StartNewLegAtBar(int startBar, LegDirectionOpt newDir, double lastPrice)
		{
		legStartBar = Math.Max(0, startBar);

		_needLegRebuild = true;
		if (IsFinalHistBar()) FlushPendingRebuilds();

			legDir = newDir;

			// Reset extremes for the new leg so next reversal works correctly
			if (newDir == LegDirectionOpt.Down)
			{
				// anchor from swing high
				legLow = lastPrice;
				legLowBar = CurrentBar;
				// keep legHigh/legHighBar as swing high already known
			}
			else if (newDir == LegDirectionOpt.Up)
			{
				// anchor from swing low
				legHigh = lastPrice;
				legHighBar = CurrentBar;
				// keep legLow/legLowBar as swing low already known
			}
			else
			{
				legHigh = High[0];
				legLow  = Low[0];
				legHighBar = CurrentBar;
				legLowBar  = CurrentBar;
			}
		}

		private void EnsureBarMaps(int primaryBarIndex)
		{
			while (barDeltaMaps.Count <= primaryBarIndex)
				barDeltaMaps.Add(new Dictionary<double, long>());

		while (barVolMaps.Count <= primaryBarIndex)
			barVolMaps.Add(new Dictionary<double, long>());
	}

		// P0-opt : en historique, seul l'etat final compte. -2 car Count inclut la
		// barre en formation, jamais traitee en historique (cf. Opt prior).
		private bool IsFinalHistBar()
		{
			return State != State.Historical || CurrentBar >= BarsArray[0].Count - 2;
		}

		private void FlushPendingRebuilds()
		{
			if (_needSessionRebuild)
			{
				RebuildSessionFrom(sessionStartBar);
				_needSessionRebuild = false;
			}
			if (_needLegRebuild)
			{
				RebuildLegFromStart(legStartBar);
				_needLegRebuild = false;
			}
		}

		private void ProcessTickIntoPrimaryBar()
		{
			int primaryIndex = BarsArray[0].GetBar(Time[0]);
			if (primaryIndex < 0) return;

			if (ShowProgressPrints && State == State.Historical)
			{
				if (!_progStarted)
				{
					_progStarted = true;
					_tickCount = 0;
					_tickNext = 500000;
					_swTotal.Reset(); _swTotal.Start();
					_progBars = BarsArray[0].Count;
					try { Print(string.Format("[LEG] chargement debut, ticks en cours... (primaires={0}) {1:HH:mm:ss}", _progBars, DateTime.Now)); } catch {}
				}
				_tickCount++;
				if (_tickCount >= _tickNext)
				{
					try { Print(string.Format("[LEG] {0} ticks traites t={1:F0}s", _tickCount, _swTotal.Elapsed.TotalSeconds)); } catch {}
					_tickNext += 500000;
				}
			}

		lock (_sync)
		{
		EnsureBarMaps(primaryIndex);

			double last = Close[0];
			long vol    = (long)Volume[0];
		if (vol <= 0) return;

		double p = Instrument.MasterInstrument.RoundToTickSize(last);

		// Appartenance a la session selectionnee (Eth/Rth) : hors session ->
		// maps de session ignorees, sinon le soir/overnight d'un chart ETH
		// contaminerait le profil RTH. Les maps de leg gardent tous les ticks
		// (la jambe en cours traverse les bornes). Maintenu quel que soit le
		// ProfileMode affiche pour un changement de mode sans etalement.
		bool sessionTick = true;
		if (SessionHours != SessionHoursOpt.Chart)
		{
			try
			{
				EnsureSessionSchedule();
				sessionTick = GetSessionStart(Time[0]).HasValue;
			}
			catch { sessionTick = true; }
		}

		// ---------- VOLUME ----------
			var vmap = barVolMaps[primaryIndex];
			if (vmap.TryGetValue(p, out long vExisting))
				vmap[p] = vExisting + vol;
			else
				vmap[p] = vol;

			int legStart = legStartBar >= 0
				? legStartBar
				: Math.Max(0, (BarsArray[0].Count - 1) - RebuildLookbackBarsCap);

			if (primaryIndex >= legStart)
			{
				if (legVolByPrice.TryGetValue(p, out long lv))
					legVolByPrice[p] = lv + vol;
				else
					legVolByPrice[p] = vol;
			}

		if (sessionTick && sessionStartBar >= 0 && primaryIndex >= sessionStartBar)
		{
			if (sessionVolByPrice.TryGetValue(p, out long sv))
					sessionVolByPrice[p] = sv + vol;
				else
					sessionVolByPrice[p] = vol;
			}

		// ---------- DELTA ----------
		long signed = 0;

		if (!double.IsNaN(lastAsk) && !double.IsNaN(lastBid) && lastAsk > 0 && lastBid > 0 && lastAsk >= lastBid)
		{
			if (last >= lastAsk)
				signed = +vol;
			else if (last <= lastBid)
				signed = -vol;
			else
			{
				if (!double.IsNaN(prevLast))
					signed = (last > prevLast) ? +vol : (last < prevLast ? -vol : 0);
			}
		}
		else
		{
			if (!double.IsNaN(prevLast))
				signed = (last > prevLast) ? +vol : (last < prevLast ? -vol : 0);
		}

		prevLast = last;
		if (signed == 0) return;

			var dmap = barDeltaMaps[primaryIndex];
			if (dmap.TryGetValue(p, out long dExisting))
				dmap[p] = dExisting + signed;
			else
				dmap[p] = signed;

			if (primaryIndex >= legStart)
			{
				if (legDeltaByPrice.TryGetValue(p, out long ld))
					legDeltaByPrice[p] = ld + signed;
				else
					legDeltaByPrice[p] = signed;
			}

		if (sessionTick && sessionStartBar >= 0 && primaryIndex >= sessionStartBar)
		{
			if (sessionDeltaByPrice.TryGetValue(p, out long sd))
					sessionDeltaByPrice[p] = sd + signed;
				else
					sessionDeltaByPrice[p] = signed;
			}
			}
		}

		// Barre entierement hors session selectionnee ? (heure d'ouverture).
	// Utilise par RebuildSessionFrom, miroir du filtre tick de l'ingestion.
	private bool BarInSelectedSession(int barIndex)
	{
		try
		{
			if (BarsArray[0] == null) return true;
			DateTime bt = BarsArray[0].GetTime(barIndex);
			return GetSessionStart(bt).HasValue;
		}
		catch { return true; }
	}

	private void RebuildLegFromStart(int startBar)
		{
			lock (_sync)
			{
			legDeltaByPrice.Clear();
			legVolByPrice.Clear();

			int end   = BarsArray[0].Count - 1;
			int start = Math.Max(0, startBar);

			if (end - start > RebuildLookbackBarsCap)
				start = end - RebuildLookbackBarsCap;

			for (int b = start; b <= end; b++)
			{
				if (b < 0) continue;
				if (b >= barDeltaMaps.Count) continue;
				if (b >= barVolMaps.Count) continue;

				foreach (var kv in barDeltaMaps[b])
				{
					if (legDeltaByPrice.TryGetValue(kv.Key, out long existing))
						legDeltaByPrice[kv.Key] = existing + kv.Value;
					else
						legDeltaByPrice[kv.Key] = kv.Value;
				}

				foreach (var kv in barVolMaps[b])
				{
					if (legVolByPrice.TryGetValue(kv.Key, out long existing))
						legVolByPrice[kv.Key] = existing + kv.Value;
					else
						legVolByPrice[kv.Key] = kv.Value;
				}
			}
			}
		}

		private void RebuildSessionFrom(int startBar)
		{
			lock (_sync)
			{
				sessionDeltaByPrice.Clear();
				sessionVolByPrice.Clear();

			int end   = BarsArray[0].Count - 1;
			int start = Math.Max(0, startBar);

			// Meme fenetre que l'original et que le leg : clamp au cap.
			if (end - start > RebuildLookbackBarsCap)
				start = end - RebuildLookbackBarsCap;

			// Filtre Eth/Rth (miroir de l'ingestion) : les bar-maps contiennent
			// tous les ticks, on saute ici les barres hors session selectionnee.
			bool filterSession = SessionHours != SessionHoursOpt.Chart;
			if (filterSession)
			{
				try { EnsureSessionSchedule(); } catch { }
			}

			for (int b = start; b <= end; b++)
		{
			if (b < 0) continue;
			if (b >= barDeltaMaps.Count) continue;
			if (b >= barVolMaps.Count) continue;
			if (filterSession && !BarInSelectedSession(b)) continue;

			foreach (var kv in barDeltaMaps[b])
			{
				if (sessionDeltaByPrice.TryGetValue(kv.Key, out long existing))
					sessionDeltaByPrice[kv.Key] = existing + kv.Value;
				else
					sessionDeltaByPrice[kv.Key] = kv.Value;
			}

				foreach (var kv in barVolMaps[b])
				{
					if (sessionVolByPrice.TryGetValue(kv.Key, out long existing))
						sessionVolByPrice[kv.Key] = existing + kv.Value;
					else
						sessionVolByPrice[kv.Key] = kv.Value;
				}
			}
			}
		}

	// ----- Bornes de session Chart / ETH / RTH -----
	private bool IsNewSessionBar()
	{
		if (SessionHours == SessionHoursOpt.Chart)
			return Bars.IsFirstBarOfSession;
		try
		{
			EnsureSessionSchedule();
			if (_schedWindows == null || _schedWindows.Count == 0)
				return Bars.IsFirstBarOfSession;
			if (CurrentBar < 1)
				return Bars.IsFirstBarOfSession;
			DateTime cur  = Time[0];
			DateTime prev = Time[1];
			DateTime? sCur = GetSessionStart(cur);
			if (!sCur.HasValue)
				return false; // hors seance (week-end) : pas de reset
			DateTime? sPrev = GetSessionStart(prev);
			return !sPrev.HasValue || sCur.Value != sPrev.Value;
		}
		catch { return Bars.IsFirstBarOfSession; }
	}

	private void EnsureSessionSchedule()
	{
		if (_schedFor == SessionHours && _schedWindows != null)
			return;
		_schedFor = SessionHours;
		var wins = new List<SessionWindowOpt>();
		string item1Name = "", item2Name = "";
		int item1Count = -1, item2Count = -1;
		string pickedName = "";
		string viaSrc = "tuple";
		int rawB = 0, rawE = 0;
		bool haveRaw = false;
		try
		{
			if (SessionHours != SessionHoursOpt.Chart && Instrument != null && Instrument.MasterInstrument != null)
			{
				var ethRth = TradingHours.GetEthRth(Instrument.MasterInstrument);
				TradingHours item1 = ethRth != null ? ethRth.Item1 : null;
				TradingHours item2 = ethRth != null ? ethRth.Item2 : null;
				try { if (item1 != null) { item1Name = item1.Name ?? ""; item1Count = item1.Sessions != null ? item1.Sessions.Count : 0; } } catch { }
				try { if (item2 != null) { item2Name = item2.Name ?? ""; item2Count = item2.Sessions != null ? item2.Sessions.Count : 0; } } catch { }
				TradingHours hours = PickTemplate(item1, item2);
				// Si le retenu ne porte pas le token attendu (ex: GetEthRth
				// renvoie deux fois l'ETH), derivation par nom depuis le
				// membre connu : 'CME US Index Futures ETH' -> '... RTH'.
				if (!TemplateTokenMatches(hours, SessionHours))
				{
					TradingHours alt = TryDerivedTemplate(item1, item2, SessionHours);
					if (alt != null) { hours = alt; viaSrc = "nom-derive"; }
				}
				if (hours != null)
					pickedName = hours.Name ?? "";
				if (hours != null && hours.Sessions != null)
				{
					foreach (Session sess in hours.Sessions)
					{
						try
						{
							if (!haveRaw) { rawB = sess.BeginTime; rawE = sess.EndTime; haveRaw = true; }
							TimeSpan st = SessionFromHms(sess.BeginTime);
							TimeSpan en = SessionFromHms(sess.EndTime);
							if (st == en)
								continue;
							wins.Add(new SessionWindowOpt(sess.BeginDay, st, sess.EndDay, en));
						}
						catch { }
					}
				}
			}
		}
		catch { }
		_schedWindows = wins;
		// Diagnostic unique par construction : noms des 2 membres, template
		// retenu, entiers bruts (format d'heure) et fenetres resolues.
		try
		{
			if (SessionHours != SessionHoursOpt.Chart)
			{
				string diag = string.Format("[LEG][SESSION] SessionHours={0} item1='{1}'({2}) item2='{3}'({4}) retenu='{5}' via={6} raw={7}-{8} creneaux={9}",
					SessionHours, item1Name, item1Count, item2Name, item2Count, pickedName, viaSrc, rawB, rawE, wins.Count);
				if (wins.Count == 0)
					Print(diag + " ATTENTION -> repli sur les bornes du chart.");
				else
				{
					var first = wins[0];
					Print(diag + string.Format(" ex: {0} {1}-{2} {3}.", first.BeginDay, first.BeginTime, first.EndDay, first.EndTime));
				}
			}
		}
		catch { }
	}

	// Choix du template : token de nom d'abord (RTH/ETH), sessions non vides
	// ensuite, ordre Item1/Item2 en tie-break.
	private TradingHours PickTemplate(TradingHours item1, TradingHours item2)
	{
		bool wantRth = SessionHours == SessionHoursOpt.Rth;
		int s1 = TemplateScore(item1, wantRth);
		int s2 = TemplateScore(item2, wantRth);
		if (wantRth)
			return s2 >= s1 ? (item2 ?? item1) : item1;
		return s1 >= s2 ? (item1 ?? item2) : item2;
	}

	private static int TemplateScore(TradingHours hours, bool wantRth)
	{
		try
		{
			if (hours == null)
				return -1;
			int score = 0;
			string n = (hours.Name ?? "").ToUpperInvariant();
			if (wantRth ? n.Contains("RTH") : n.Contains("ETH"))
				score += 2;
			if (hours.Sessions != null && hours.Sessions.Count > 0)
				score += 1;
			return score;
		}
		catch { return -1; }
	}

	private static bool TemplateTokenMatches(TradingHours hours, SessionHoursOpt mode)
	{
		try
		{
			if (hours == null || string.IsNullOrEmpty(hours.Name))
				return false;
			string n = hours.Name.ToUpperInvariant();
			return mode == SessionHoursOpt.Rth ? n.Contains("RTH") : n.Contains("ETH");
		}
		catch { return false; }
	}

	// Tente le template homonyme : remplace le token dans le nom connu
	// ('CME US Index Futures ETH' -> 'CME US Index Futures RTH') et charge
	// par TradingHours.Get. Verifie token + sessions avant usage.
	private static TradingHours TryDerivedTemplate(TradingHours item1, TradingHours item2, SessionHoursOpt mode)
	{
		try
		{
			string src = (item1 != null && !string.IsNullOrEmpty(item1.Name)) ? item1.Name
				: (item2 != null ? item2.Name ?? "" : "");
			if (string.IsNullOrEmpty(src))
				return null;
			string from = mode == SessionHoursOpt.Rth ? "ETH" : "RTH";
			string to = mode == SessionHoursOpt.Rth ? "RTH" : "ETH";
			string derived = ReplaceInsensitive(src, from, to);
			if (derived == src)
				return null;
			TradingHours t = null;
			try { t = TradingHours.Get(derived); } catch { t = null; }
			if (t == null || t.Sessions == null || t.Sessions.Count == 0)
				return null;
			if (!TemplateTokenMatches(t, mode))
				return null;
			return t;
		}
		catch { return null; }
	}

	private static string ReplaceInsensitive(string src, string from, string to)
	{
		try
		{
			int i = src.ToUpperInvariant().IndexOf(from);
			if (i < 0)
				return src;
			return src.Substring(0, i) + to + src.Substring(i + from.Length);
		}
		catch { return src; }
	}

	// Debut de la seance contenant t (heure locale des barres), ou null hors
	// seance. Week-aware : gere l'overnight (fin += 7j si <= debut) et le
	// week-end (aucune fenetre ne contient t -> null, pas de reset).
	private DateTime? GetSessionStart(DateTime t)
	{
		var wins = _schedWindows;
		if (wins == null || wins.Count == 0)
			return null;
		foreach (var w in wins)
		{
			DateTime begin = PrevBeginOf(w.BeginDay, t) + w.BeginTime;
			DateTime end = PrevBeginOf(w.EndDay, t) + w.EndTime;
			if (end <= begin)
				end = end.AddDays(7);
			if (begin <= t && t < end)
				return begin;
		}
		return null;
	}

	private static DateTime PrevBeginOf(DayOfWeek day, DateTime refDate)
	{
		// Derniere occurrence de "day" a ou avant refDate.
		int diff = ((int)refDate.DayOfWeek - (int)day + 7) % 7;
		return refDate.Date.AddDays(-diff);
	}

	// NT8 : Session.BeginTime/EndTime en HHMM (ex: 1700 = 17h00, 830 = 8h30).
	private static TimeSpan SessionFromHms(int hms)
	{
		return new TimeSpan(hms / 100, hms % 100, 0);
	}

	protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			try { base.OnRender(chartControl, chartScale); }
			catch (Exception ex) { RenderPhaseError("BASE", ex); return; }

			var srcDelta = (ProfileMode == ProfileModesOpt.SessionCurrentDay) ? sessionDeltaByPrice : legDeltaByPrice;
			var srcVol   = (ProfileMode == ProfileModesOpt.SessionCurrentDay) ? sessionVolByPrice   : legVolByPrice;

			// P0-opt thread-safety : copie courte sous verrou, rendu hors verrou.
			// Sans ca, le thread SharpDX enumere pendant qu'un tick mute -> crash
			// "Collection was modified" (vu en charge, barre tick ~20M).
			bool wantVol = ShowVolumeProfile;
			Dictionary<double, long> activeDelta = null;
			Dictionary<double, long> activeVol = null;
			try
			{
				lock (_sync)
				{
					activeDelta = new Dictionary<double, long>(srcDelta);
					if (wantVol && srcVol != null)
						activeVol = new Dictionary<double, long>(srcVol);
				}
			}
			catch (Exception ex) { RenderPhaseError("COPY", ex); return; }

			bool haveDelta = activeDelta != null && activeDelta.Count > 0;
			bool haveVol   = wantVol && activeVol != null && activeVol.Count > 0;

			if (!haveDelta && !haveVol)
				return;

			try { EnsureDxResources(); }
			catch (Exception ex) { DisposeDx(); RenderPhaseError("DX", ex); return; }

			// Base spine position
			float reservedPx = (float)(ChartPanel.W * RightReservedPercent);
			float spineXBase = chartControl.CanvasRight - RightMarginPx - reservedPx - XOffsetPx;

			float panelTop    = ChartPanel.Y;
			float panelBottom = ChartPanel.Y + ChartPanel.H;

			if (ShowSpine)
			{
				var spineRect = new RectangleF(spineXBase - SpineWidthPx, panelTop, SpineWidthPx, panelBottom - panelTop);
				RenderTarget.FillRectangle(spineRect, spineBrushDx);
			}

			// Determine X anchors for Delta and Volume
			float deltaSpineX = spineXBase;
			float volSpineX   = spineXBase;

			if (haveVol && (VolumeLayer == VolumeProfileLayerOpt.LeftOfDelta || VolumeLayer == VolumeProfileLayerOpt.RightOfDelta))
			{
				if (VolumeLayer == VolumeProfileLayerOpt.LeftOfDelta)
				{
					deltaSpineX = spineXBase;
					volSpineX   = spineXBase - MaxProfileWidthPx - SideBySideGapPx;
				}
				else // RightOfDelta
				{
					volSpineX   = spineXBase;
					deltaSpineX = spineXBase - VolumeProfileWidthPx - SideBySideGapPx;
				}
			}

		// Une seule passe par couche (fix 2026-09-20 : plus de double dessin
		// delta+volume en mode side-by-side, comme DeltaProfile.cs).
		bool sideBySide = haveVol && (VolumeLayer == VolumeProfileLayerOpt.LeftOfDelta || VolumeLayer == VolumeProfileLayerOpt.RightOfDelta);
		if (haveVol && VolumeLayer == VolumeProfileLayerOpt.BehindDelta)
			try { RenderVolumeProfile(chartScale, volSpineX, activeVol); } catch (Exception ex) { DisposeDx(); RenderPhaseError("VOL", ex); }
		if (haveVol && sideBySide)
			try { RenderVolumeProfile(chartScale, volSpineX, activeVol); } catch (Exception ex) { DisposeDx(); RenderPhaseError("VOL", ex); }
		if (haveDelta)
			try { RenderDeltaProfile(chartScale, deltaSpineX, activeDelta); } catch (Exception ex) { DisposeDx(); RenderPhaseError("DELTA", ex); }
		if (haveVol && VolumeLayer == VolumeProfileLayerOpt.InFrontOfDelta)
			try { RenderVolumeProfile(chartScale, volSpineX, activeVol); } catch (Exception ex) { DisposeDx(); RenderPhaseError("VOL", ex); }
	}

		private void RenderDeltaProfile(ChartScale chartScale, float spineX, Dictionary<double, long> activeMap)
		{
			if (activeMap == null || activeMap.Count == 0)
				return;

			double tick = TickSize;
			if (tick <= 0)
				return;

			// Regroupement : zoom auto (comportement historique) OU ticks/niveau.
			int groupTicks = 1;
			if (GroupByZoom)
			{
				float tickPx = Math.Abs(chartScale.GetYByValue(tick) - chartScale.GetYByValue(0));
				if (tickPx > 0 && tickPx < MinRowHeightPx)
					groupTicks = (int)Math.Ceiling(MinRowHeightPx / tickPx);
			}
			else
			{
				groupTicks = Math.Max(1, TicksPerLevel);
			}

			// Min/max en un seul passage (sans LINQ, sans alloc).
			bool any = false;
			double rawMax = 0, rawMin = 0;
			foreach (double k in activeMap.Keys)
			{
				if (double.IsNaN(k) || double.IsInfinity(k))
					continue;
				if (!any) { rawMax = k; rawMin = k; any = true; }
				else { if (k > rawMax) rawMax = k; if (k < rawMin) rawMin = k; }
			}
			if (!any)
				return;

			double pTop0  = Instrument.MasterInstrument.RoundToTickSize(rawMax);
			double pFloor = Instrument.MasterInstrument.RoundToTickSize(rawMin);

			double binSpan = tick * groupTicks;
			int binCount = (int)Math.Round((pTop0 - pFloor) / binSpan) + 1;
			if (binCount <= 0)
				return;
			// Garde-fou : plage aberrante -> elargit le groupement au lieu d'allouer.
			if (binCount > 50000)
			{
				groupTicks = Math.Max(groupTicks, (int)Math.Ceiling((pTop0 - pFloor) / (tick * 50000)));
				binSpan = tick * groupTicks;
				binCount = (int)Math.Round((pTop0 - pFloor) / binSpan) + 1;
				if (binCount <= 0 || binCount > 50000)
					return;
			}

			// Single pass : bins + max (remplace ComputeMaxAbsBin, plus de double parcours).
			long[] bins = new long[binCount];
			long maxAbsBin = 0;
			for (int i = 0; i < binCount; i++)
			{
				double pTop = pTop0 - binSpan * i;
				long sum = 0;
				for (int g = 0; g < groupTicks; g++)
				{
					double p = pTop - tick * g;
					long d;
					if (activeMap.TryGetValue(p, out d))
						sum += d;
				}
				bins[i] = sum;
				long a = sum >= 0 ? sum : -sum;
				if (a > maxAbsBin)
					maxAbsBin = a;
			}
			if (maxAbsBin < MinAbsDeltaToShow)
				return;

			float approxRowHeightPx = Math.Abs(chartScale.GetYByValue(pTop0) - chartScale.GetYByValue(pTop0 - binSpan));
			int baseFont = GetEffectiveFontSizeFromRowHeight(approxRowHeightPx);
			if (baseFont > 0 && !AdaptiveDeltaText)
				EnsureTextFormat(baseFont);

			float panelTop    = ChartPanel.Y;
			float panelBottom = ChartPanel.Y + ChartPanel.H;

			for (int i = 0; i < binCount; i++)
			{
				long binDelta = bins[i];
				long absD = binDelta >= 0 ? binDelta : -binDelta;
				if (absD < MinAbsDeltaToShow)
					continue;

				double pTop = pTop0 - binSpan * i;
				float yTop = chartScale.GetYByValue(pTop);
				float yBot = chartScale.GetYByValue(pTop - binSpan);
				float top = Math.Min(yTop, yBot);
				float height = Math.Abs(yBot - yTop);
				if (height < 2f) height = 2f;

				// Clip vertical : ignore les lignes hors panneau (sessions longues).
				if (top > panelBottom || top + height < panelTop)
					continue;

				float width = (float)(MaxProfileWidthPx * (absD / (double)maxAbsBin));
				if (width <= 0.5f)
					continue;

				var rect = new RectangleF(spineX - width, top, width, height);
				RenderTarget.FillRectangle(rect, binDelta >= 0 ? posBrushDx : negBrushDx);
				if (ShowDeltaBorder && DeltaBorderWidthPx > 0 && borderBrushDx != null)
				{
					try { RenderTarget.DrawRectangle(rect, borderBrushDx, DeltaBorderWidthPx); } catch { }
				}

				if (baseFont <= 0)
					continue;

				string txt = AdaptiveDeltaText ? FormatDeltaAdaptive(binDelta) : binDelta.ToString();
				if (AdaptiveDeltaText)
					DrawDeltaTextAdaptive(txt, rect, height, baseFont);
				else if (height >= (baseFont + 2))
				{
					float textWidth = MeasureTextWidth(txt);
					if (rect.Width >= textWidth + 6f)
						RenderTarget.DrawText(txt, textFormat, rect, textBrushDx);
				}
			}
		}

		// ----- Texte delta adaptatif : abreviations k/M + police reduite pour tenir -----
		private void DrawDeltaTextAdaptive(string txt, RectangleF rect, float height, int baseFont)
		{
			int minF = Math.Max(1, AutoFontMin);
			for (int f = baseFont; f >= minF; f--)
			{
				if (height < f + 2)
					continue;
				TextFormat tf = GetCachedFont(f);
				if (tf == null)
					continue;
				float w = MeasureTextWidthCached(txt, tf, f);
				if (rect.Width >= w + 6f)
				{
					try { RenderTarget.DrawText(txt, tf, rect, textBrushDx); } catch { }
					return;
				}
			}
		}

		private TextFormat GetCachedFont(int fontSize)
		{
			try
			{
				TextFormat tf;
				if (_fontCache.TryGetValue(fontSize, out tf) && tf != null && !tf.IsDisposed)
					return tf;
				tf = new TextFormat(Core.Globals.DirectWriteFactory, "Segoe UI", fontSize)
				{
					TextAlignment = SharpDX.DirectWrite.TextAlignment.Center,
					ParagraphAlignment = ParagraphAlignment.Center
				};
				_fontCache[fontSize] = tf;
				return tf;
			}
			catch { return null; }
		}

		private float MeasureTextWidthCached(string text, TextFormat fmt, int fontSize)
		{
			try
			{
				string key = fontSize + "|" + text;
				float w;
				if (_textWidthCache.TryGetValue(key, out w))
					return w;
				if (fmt == null)
					return 0f;
				using (var layout = new TextLayout(
					Core.Globals.DirectWriteFactory,
					text,
					fmt,
					1000,
					100))
				{
					w = layout.Metrics.Width;
				}
				if (_textWidthCache.Count > 2000)
					_textWidthCache.Clear();
				_textWidthCache[key] = w;
				return w;
			}
			catch { return 0f; }
		}

		private static string FormatDeltaAdaptive(long v)
		{
			long a = v >= 0 ? v : -v;
			if (a >= 1000000L)
				return Trim1(v / 1000000.0) + "M";
			if (a >= 10000L)
				return Trim1(v / 1000.0) + "k";
			return v.ToString();
		}

		private static string Trim1(double d)
		{
			double r = Math.Round(d, 1);
			long whole = (long)r;
			if (r == whole)
				return whole.ToString();
			return r.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
		}

		private int GetEffectiveFontSizeFromRowHeight(float rowHeightPx)
		{
			// "Texte visible" decoché = jamais de chiffres (barres seules).
			if (!ShowDeltaText)
				return 0;

			if (!AutoDeltaText)
				return FontSize;

			// If rows are tiny, hide text entirely
			if (rowHeightPx < 6f)
				return 0;

			// heuristic: text should be ~70% of row height
			int f = (int)Math.Floor(rowHeightPx * 0.70f);

			if (f < AutoFontMin)
				f = AutoFontMin;

			if (f > AutoFontMax)
				f = AutoFontMax;

			if (rowHeightPx < (f + 2))
				return 0;

			return f;
		}

		private void EnsureTextFormat(int effectiveFont)
		{
			if (effectiveFont <= 0)
				return;

			if (textFormat == null || lastFontSize != effectiveFont)
			{
				textFormat?.Dispose();
				textFormat = new TextFormat(Core.Globals.DirectWriteFactory, "Segoe UI", effectiveFont)
				{
					TextAlignment = SharpDX.DirectWrite.TextAlignment.Center,
					ParagraphAlignment = ParagraphAlignment.Center
				};
				lastFontSize = effectiveFont;
			}
		}

		private void RenderVolumeProfile(ChartScale chartScale, float spineX, Dictionary<double, long> volMap)
		{
			long maxVol = 0;
			foreach (var kv in volMap)
				maxVol = Math.Max(maxVol, kv.Value);

			if (maxVol <= 0)
				return;

			double maxPrice = Instrument.MasterInstrument.RoundToTickSize(volMap.Keys.Max());
			double minPrice = Instrument.MasterInstrument.RoundToTickSize(volMap.Keys.Min());

			double pTop = maxPrice;
			while (pTop >= minPrice - TickSize * 0.5)
			{
				if (volMap.TryGetValue(pTop, out long v) && v > 0)
				{
					float yTop = chartScale.GetYByValue(pTop);
					float yBot = chartScale.GetYByValue(pTop - TickSize);
					float height = Math.Abs(yBot - yTop);
					if (height < 1f) height = 1f;

					float width = (float)(VolumeProfileWidthPx * (v / (double)maxVol));

					if (width > 0.5f)
					{
						float top = Math.Min(yTop, yBot);
						var rect = new RectangleF(spineX - width, top, width, height);
						RenderTarget.FillRectangle(rect, volBrushDx);
					}
				}

				pTop -= TickSize;
			}
		}

		private void EnsureDxResources()
		{
			string posSer = SafeBrushSerialize(PositiveBrush);
			string negSer = SafeBrushSerialize(NegativeBrush);
			string txtSer = SafeBrushSerialize(TextBrush);
			string volSer = SafeBrushSerialize(VolumeBrush);
			string borderSer = SafeBrushSerialize(DeltaBorderBrush);

			bool needsRebuild =
				posBrushDx == null || negBrushDx == null || textBrushDx == null || spineBrushDx == null || volBrushDx == null || borderBrushDx == null
				|| lastPosBrushSer != posSer
				|| lastNegBrushSer != negSer
				|| lastTextBrushSer != txtSer
				|| lastVolBrushSer != volSer
				|| lastBorderBrushSer != borderSer
				|| Math.Abs(lastDeltaOpacity - DeltaOpacity) > 0.0001f
				|| Math.Abs(lastVolOpacity - VolumeOpacity) > 0.0001f;

			if (!needsRebuild)
				return;

			try
			{
				posBrushDx?.Dispose();
				negBrushDx?.Dispose();
				textBrushDx?.Dispose();
				spineBrushDx?.Dispose();
				volBrushDx?.Dispose();
				borderBrushDx?.Dispose();
			}
			catch { }

			posBrushDx   = new SolidColorBrush(RenderTarget, ToDx(PositiveBrush, DeltaOpacity));
			negBrushDx   = new SolidColorBrush(RenderTarget, ToDx(NegativeBrush, DeltaOpacity));
			textBrushDx  = new SolidColorBrush(RenderTarget, ToDx(TextBrush, 1f));
			spineBrushDx = new SolidColorBrush(RenderTarget, new Color4(1f, 1f, 1f, 0.25f));
			volBrushDx   = new SolidColorBrush(RenderTarget, ToDx(VolumeBrush, VolumeOpacity));
			borderBrushDx = new SolidColorBrush(RenderTarget, ToDx(DeltaBorderBrush, 1f));

			lastPosBrushSer = posSer;
			lastNegBrushSer = negSer;
			lastTextBrushSer = txtSer;
			lastVolBrushSer = volSer;
			lastBorderBrushSer = borderSer;
			lastDeltaOpacity = DeltaOpacity;
			lastVolOpacity = VolumeOpacity;
		}

		private string SafeBrushSerialize(WpfBrush b)
		{
			try { return Serialize.BrushToString(b); }
			catch { return b?.ToString() ?? ""; }
		}

		public override void OnRenderTargetChanged()
		{
			DisposeDx();
			base.OnRenderTargetChanged();
		}

		private void RenderPhaseError(string phase, Exception ex)
		{
			try
			{
				if (_renderErrCount < 10)
					Print(string.Format("[LEG][RENDER] phase={0} {1}: {2}", phase, ex.GetType().Name, ex.Message));
				_renderErrCount++;
			}
			catch { }
		}

		private float MeasureTextWidth(string text)
		{
			if (textFormat == null)
				return 0f;

			using (var layout = new TextLayout(
				Core.Globals.DirectWriteFactory,
				text,
				textFormat,
				1000,
				100))
			{
				return layout.Metrics.Width;
			}
		}

		private static System.Windows.Media.Color BrushToMediaColor(WpfBrush b)
		{
			if (b is WpfSolidColorBrush sb)
				return sb.Color;
			return WpfColors.White;
		}

		private Color4 ToDx(WpfBrush b, float alphaMult)
		{
			var c = BrushToMediaColor(b ?? WpfBrushes.White);
			return new Color4(c.R / 255f, c.G / 255f, c.B / 255f, (c.A / 255f) * alphaMult);
		}

		#region Properties

		[NinjaScriptProperty]
		[Display(Name = "Profile Mode", Order = 0, GroupName = "Mode")]
		public ProfileModesOpt ProfileMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Session Hours", Order = 1, GroupName = "Mode",
			Description = "Chart = bornes du template du chart (comportement d'origine). Eth/Rth = templates NT8 de l'instrument (TradingHours.GetEthRth).")]
		public SessionHoursOpt SessionHours { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Rotation Mode", Order=0, GroupName="Leg Detection",
			Description="DistanceFromExtreme = new profile when price is X away from leg high/low. TrueSwingAlternate = only on reversal X, alternates direction (new leg starts at swing extreme bar).")]
		public LegRotationModeOpt RotationMode { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, 5000.0)]
		[Display(Name = "Rotation (Points)", Order = 1, GroupName = "Leg Detection",
			Description = "Start a new leg profile when price rotates by this many POINTS. Set to 0 to disable.")]
		public double RotationPoints { get; set; }

		// ===== Delta rendering =====
		[NinjaScriptProperty]
		[Range(20, 600)]
		[Display(Name = "Max Delta Width (px)", Order = 2, GroupName = "Delta Rendering")]
		public int MaxProfileWidthPx { get; set; }

		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Min Abs Delta To Show", Order = 3, GroupName = "Delta Rendering")]
		public long MinAbsDeltaToShow { get; set; }

		[NinjaScriptProperty]
		[Range(200, 50000)]
		[Display(Name = "Rebuild Lookback Cap (bars)", Order = 4, GroupName = "Performance")]
		public int RebuildLookbackBarsCap { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Prints de progression", Description = "Heartbeat tous les ~500k ticks + bilan au chargement historique dans Output. True recommande pour diagnostiquer, false pour silence total.", Order = 5, GroupName = "Performance")]
		public bool ShowProgressPrints { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Grouper par zoom", Order=4, GroupName="Delta Rendering",
			Description="Coche = regroupement auto selon le zoom via Min Row Height (comportement actuel). Decoche = Ticks Per Level.")]
		public bool GroupByZoom { get; set; }

		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name="Ticks Per Level", Order=5, GroupName="Delta Rendering",
			Description="Ticks agreges par ligne quand Grouper par zoom = false.")]
		public int TicksPerLevel { get; set; }

		// ===== Auto text settings =====
		[NinjaScriptProperty]
		[Display(Name="Auto Delta Text", Order=5, GroupName="Delta Rendering",
			Description="When enabled, font size (and whether text is shown) is automatically derived from current row height / zoom.")]
		public bool AutoDeltaText { get; set; }

		[NinjaScriptProperty]
		[Range(6, 30)]
		[Display(Name="Auto Font Min", Order=6, GroupName="Delta Rendering")]
		public int AutoFontMin { get; set; }

		[NinjaScriptProperty]
		[Range(6, 40)]
		[Display(Name="Auto Font Max", Order=7, GroupName="Delta Rendering")]
		public int AutoFontMax { get; set; }

		[NinjaScriptProperty]
		[Range(8, 28)]
		[Display(Name="Manual Font Size", Order=8, GroupName="Delta Rendering",
			Description="Used only when Auto Delta Text = false.")]
		public int FontSize { get; set; }

		[NinjaScriptProperty]
		[Range(2, 30)]
		[Display(Name="Min Row Height (px)", Order=9, GroupName="Delta Rendering",
			Description="Auto-groups ticks per row when zoomed out to avoid overlap (DELTA only, used only when Grouper par zoom is checked).")]
		public int MinRowHeightPx { get; set; }

		// ===== Delta Colors =====
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Positive Brush", Order = 10, GroupName = "Delta Colors")]
		public WpfBrush PositiveBrush { get; set; }

		[Browsable(false)]
		public string PositiveBrushSerialize
		{
			get { return Serialize.BrushToString(PositiveBrush); }
			set { PositiveBrush = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Negative Brush", Order = 11, GroupName = "Delta Colors")]
		public WpfBrush NegativeBrush { get; set; }

		[Browsable(false)]
		public string NegativeBrushSerialize
		{
			get { return Serialize.BrushToString(NegativeBrush); }
			set { NegativeBrush = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Text Brush", Order = 12, GroupName = "Delta Colors")]
		public WpfBrush TextBrush { get; set; }

		[Browsable(false)]
		public string TextBrushSerialize
		{
			get { return Serialize.BrushToString(TextBrush); }
			set { TextBrush = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Range(0.1, 1.0)]
		[Display(Name = "Delta Opacity", Order = 13, GroupName = "Delta Rendering")]
		public float DeltaOpacity { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Texte visible", Description = "Coche = chiffres delta affiches (comportement actuel). Decoche = barres seules, jamais de chiffres.", Order = 14, GroupName = "Delta Rendering")]
		public bool ShowDeltaText { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Texte adaptatif", Description = "Coche = nombres abreges (12.5k / 1.2M) + police reduite pour tenir dans la barre. Decoche = chiffres entiers (comportement actuel).", Order = 15, GroupName = "Delta Rendering")]
		public bool AdaptiveDeltaText { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Bordure colonnes", Description = "Coche = bordure autour de chaque colonne delta. Decoche = barres pleines sans contour.", Order = 16, GroupName = "Delta Rendering")]
		public bool ShowDeltaBorder { get; set; }

		[NinjaScriptProperty]
		[Range(1, 3)]
		[Display(Name = "Largeur bordure (px)", Order = 17, GroupName = "Delta Rendering")]
		public int DeltaBorderWidthPx { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Couleur bordure", Order = 18, GroupName = "Delta Rendering")]
		public WpfBrush DeltaBorderBrush { get; set; }

		[Browsable(false)]
		public string DeltaBorderBrushSerialize
		{
			get { return Serialize.BrushToString(DeltaBorderBrush); }
			set { DeltaBorderBrush = Serialize.StringToBrush(value); }
		}

		// ===== Shared rendering / positioning =====
		[NinjaScriptProperty]
		[Display(Name = "Show Spine", Order = 0, GroupName = "Rendering")]
		public bool ShowSpine { get; set; }

		[NinjaScriptProperty]
		[Range(1, 6)]
		[Display(Name = "Spine Width (px)", Order = 1, GroupName = "Rendering")]
		public int SpineWidthPx { get; set; }

		[NinjaScriptProperty]
		[Range(0, 120)]
		[Display(Name = "Right Margin (px)", Order = 2, GroupName = "Rendering")]
		public int RightMarginPx { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, 0.8)]
		[Display(Name = "Right Reserved %", Order = 3, GroupName = "Rendering")]
		public double RightReservedPercent { get; set; }

		[NinjaScriptProperty]
		[Range(-500, 500)]
		[Display(Name = "X Offset (px)", Order = 4, GroupName = "Rendering")]
		public int XOffsetPx { get; set; }

		// ===== Volume Profile Overlay =====
		[NinjaScriptProperty]
		[Display(Name = "Show Volume Profile", Order = 0, GroupName = "Volume Profile")]
		public bool ShowVolumeProfile { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Volume Placement", Order = 1, GroupName = "Volume Profile")]
		public VolumeProfileLayerOpt VolumeLayer { get; set; }

		[NinjaScriptProperty]
		[Range(20, 600)]
		[Display(Name = "Volume Width (px)", Order = 2, GroupName = "Volume Profile")]
		public int VolumeProfileWidthPx { get; set; }

		[NinjaScriptProperty]
		[Range(0.05, 1.0)]
		[Display(Name = "Volume Opacity", Order = 3, GroupName = "Volume Profile")]
		public float VolumeOpacity { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Volume Brush", Order = 4, GroupName = "Volume Profile")]
		public WpfBrush VolumeBrush { get; set; }

		[Browsable(false)]
		public string VolumeBrushSerialize
		{
			get { return Serialize.BrushToString(VolumeBrush); }
			set { VolumeBrush = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Range(0, 50)]
		[Display(Name = "Side-by-Side Gap (px)", Order = 5, GroupName = "Volume Profile")]
		public int SideBySideGapPx { get; set; }

		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private LegToLegDeltaProfile_Opt[] cacheLegToLegDeltaProfile_Opt;
		public LegToLegDeltaProfile_Opt LegToLegDeltaProfile_Opt(ProfileModesOpt profileMode, SessionHoursOpt sessionHours, LegRotationModeOpt rotationMode, double rotationPoints, int maxProfileWidthPx, long minAbsDeltaToShow, int rebuildLookbackBarsCap, bool showProgressPrints, bool groupByZoom, int ticksPerLevel, bool autoDeltaText, int autoFontMin, int autoFontMax, int fontSize, int minRowHeightPx, WpfBrush positiveBrush, WpfBrush negativeBrush, WpfBrush textBrush, float deltaOpacity, bool showDeltaText, bool adaptiveDeltaText, bool showDeltaBorder, int deltaBorderWidthPx, WpfBrush deltaBorderBrush, bool showSpine, int spineWidthPx, int rightMarginPx, double rightReservedPercent, int xOffsetPx, bool showVolumeProfile, VolumeProfileLayerOpt volumeLayer, int volumeProfileWidthPx, float volumeOpacity, WpfBrush volumeBrush, int sideBySideGapPx)
		{
			return LegToLegDeltaProfile_Opt(Input, profileMode, sessionHours, rotationMode, rotationPoints, maxProfileWidthPx, minAbsDeltaToShow, rebuildLookbackBarsCap, showProgressPrints, groupByZoom, ticksPerLevel, autoDeltaText, autoFontMin, autoFontMax, fontSize, minRowHeightPx, positiveBrush, negativeBrush, textBrush, deltaOpacity, showDeltaText, adaptiveDeltaText, showDeltaBorder, deltaBorderWidthPx, deltaBorderBrush, showSpine, spineWidthPx, rightMarginPx, rightReservedPercent, xOffsetPx, showVolumeProfile, volumeLayer, volumeProfileWidthPx, volumeOpacity, volumeBrush, sideBySideGapPx);
		}

		public LegToLegDeltaProfile_Opt LegToLegDeltaProfile_Opt(ISeries<double> input, ProfileModesOpt profileMode, SessionHoursOpt sessionHours, LegRotationModeOpt rotationMode, double rotationPoints, int maxProfileWidthPx, long minAbsDeltaToShow, int rebuildLookbackBarsCap, bool showProgressPrints, bool groupByZoom, int ticksPerLevel, bool autoDeltaText, int autoFontMin, int autoFontMax, int fontSize, int minRowHeightPx, WpfBrush positiveBrush, WpfBrush negativeBrush, WpfBrush textBrush, float deltaOpacity, bool showDeltaText, bool adaptiveDeltaText, bool showDeltaBorder, int deltaBorderWidthPx, WpfBrush deltaBorderBrush, bool showSpine, int spineWidthPx, int rightMarginPx, double rightReservedPercent, int xOffsetPx, bool showVolumeProfile, VolumeProfileLayerOpt volumeLayer, int volumeProfileWidthPx, float volumeOpacity, WpfBrush volumeBrush, int sideBySideGapPx)
		{
			if (cacheLegToLegDeltaProfile_Opt != null)
				for (int idx = 0; idx < cacheLegToLegDeltaProfile_Opt.Length; idx++)
					if (cacheLegToLegDeltaProfile_Opt[idx] != null && cacheLegToLegDeltaProfile_Opt[idx].ProfileMode == profileMode && cacheLegToLegDeltaProfile_Opt[idx].SessionHours == sessionHours && cacheLegToLegDeltaProfile_Opt[idx].RotationMode == rotationMode && cacheLegToLegDeltaProfile_Opt[idx].RotationPoints == rotationPoints && cacheLegToLegDeltaProfile_Opt[idx].MaxProfileWidthPx == maxProfileWidthPx && cacheLegToLegDeltaProfile_Opt[idx].MinAbsDeltaToShow == minAbsDeltaToShow && cacheLegToLegDeltaProfile_Opt[idx].RebuildLookbackBarsCap == rebuildLookbackBarsCap && cacheLegToLegDeltaProfile_Opt[idx].ShowProgressPrints == showProgressPrints && cacheLegToLegDeltaProfile_Opt[idx].GroupByZoom == groupByZoom && cacheLegToLegDeltaProfile_Opt[idx].TicksPerLevel == ticksPerLevel && cacheLegToLegDeltaProfile_Opt[idx].AutoDeltaText == autoDeltaText && cacheLegToLegDeltaProfile_Opt[idx].AutoFontMin == autoFontMin && cacheLegToLegDeltaProfile_Opt[idx].AutoFontMax == autoFontMax && cacheLegToLegDeltaProfile_Opt[idx].FontSize == fontSize && cacheLegToLegDeltaProfile_Opt[idx].MinRowHeightPx == minRowHeightPx && cacheLegToLegDeltaProfile_Opt[idx].PositiveBrush == positiveBrush && cacheLegToLegDeltaProfile_Opt[idx].NegativeBrush == negativeBrush && cacheLegToLegDeltaProfile_Opt[idx].TextBrush == textBrush && cacheLegToLegDeltaProfile_Opt[idx].DeltaOpacity == deltaOpacity && cacheLegToLegDeltaProfile_Opt[idx].ShowDeltaText == showDeltaText && cacheLegToLegDeltaProfile_Opt[idx].AdaptiveDeltaText == adaptiveDeltaText && cacheLegToLegDeltaProfile_Opt[idx].ShowDeltaBorder == showDeltaBorder && cacheLegToLegDeltaProfile_Opt[idx].DeltaBorderWidthPx == deltaBorderWidthPx && cacheLegToLegDeltaProfile_Opt[idx].DeltaBorderBrush == deltaBorderBrush && cacheLegToLegDeltaProfile_Opt[idx].ShowSpine == showSpine && cacheLegToLegDeltaProfile_Opt[idx].SpineWidthPx == spineWidthPx && cacheLegToLegDeltaProfile_Opt[idx].RightMarginPx == rightMarginPx && cacheLegToLegDeltaProfile_Opt[idx].RightReservedPercent == rightReservedPercent && cacheLegToLegDeltaProfile_Opt[idx].XOffsetPx == xOffsetPx && cacheLegToLegDeltaProfile_Opt[idx].ShowVolumeProfile == showVolumeProfile && cacheLegToLegDeltaProfile_Opt[idx].VolumeLayer == volumeLayer && cacheLegToLegDeltaProfile_Opt[idx].VolumeProfileWidthPx == volumeProfileWidthPx && cacheLegToLegDeltaProfile_Opt[idx].VolumeOpacity == volumeOpacity && cacheLegToLegDeltaProfile_Opt[idx].VolumeBrush == volumeBrush && cacheLegToLegDeltaProfile_Opt[idx].SideBySideGapPx == sideBySideGapPx && cacheLegToLegDeltaProfile_Opt[idx].EqualsInput(input))
						return cacheLegToLegDeltaProfile_Opt[idx];
			return CacheIndicator<LegToLegDeltaProfile_Opt>(new LegToLegDeltaProfile_Opt(){ ProfileMode = profileMode, SessionHours = sessionHours, RotationMode = rotationMode, RotationPoints = rotationPoints, MaxProfileWidthPx = maxProfileWidthPx, MinAbsDeltaToShow = minAbsDeltaToShow, RebuildLookbackBarsCap = rebuildLookbackBarsCap, ShowProgressPrints = showProgressPrints, GroupByZoom = groupByZoom, TicksPerLevel = ticksPerLevel, AutoDeltaText = autoDeltaText, AutoFontMin = autoFontMin, AutoFontMax = autoFontMax, FontSize = fontSize, MinRowHeightPx = minRowHeightPx, PositiveBrush = positiveBrush, NegativeBrush = negativeBrush, TextBrush = textBrush, DeltaOpacity = deltaOpacity, ShowDeltaText = showDeltaText, AdaptiveDeltaText = adaptiveDeltaText, ShowDeltaBorder = showDeltaBorder, DeltaBorderWidthPx = deltaBorderWidthPx, DeltaBorderBrush = deltaBorderBrush, ShowSpine = showSpine, SpineWidthPx = spineWidthPx, RightMarginPx = rightMarginPx, RightReservedPercent = rightReservedPercent, XOffsetPx = xOffsetPx, ShowVolumeProfile = showVolumeProfile, VolumeLayer = volumeLayer, VolumeProfileWidthPx = volumeProfileWidthPx, VolumeOpacity = volumeOpacity, VolumeBrush = volumeBrush, SideBySideGapPx = sideBySideGapPx }, input, ref cacheLegToLegDeltaProfile_Opt);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.LegToLegDeltaProfile_Opt LegToLegDeltaProfile_Opt(ProfileModesOpt profileMode, SessionHoursOpt sessionHours, LegRotationModeOpt rotationMode, double rotationPoints, int maxProfileWidthPx, long minAbsDeltaToShow, int rebuildLookbackBarsCap, bool showProgressPrints, bool groupByZoom, int ticksPerLevel, bool autoDeltaText, int autoFontMin, int autoFontMax, int fontSize, int minRowHeightPx, WpfBrush positiveBrush, WpfBrush negativeBrush, WpfBrush textBrush, float deltaOpacity, bool showDeltaText, bool adaptiveDeltaText, bool showDeltaBorder, int deltaBorderWidthPx, WpfBrush deltaBorderBrush, bool showSpine, int spineWidthPx, int rightMarginPx, double rightReservedPercent, int xOffsetPx, bool showVolumeProfile, VolumeProfileLayerOpt volumeLayer, int volumeProfileWidthPx, float volumeOpacity, WpfBrush volumeBrush, int sideBySideGapPx)
		{
			return indicator.LegToLegDeltaProfile_Opt(Input, profileMode, sessionHours, rotationMode, rotationPoints, maxProfileWidthPx, minAbsDeltaToShow, rebuildLookbackBarsCap, showProgressPrints, groupByZoom, ticksPerLevel, autoDeltaText, autoFontMin, autoFontMax, fontSize, minRowHeightPx, positiveBrush, negativeBrush, textBrush, deltaOpacity, showDeltaText, adaptiveDeltaText, showDeltaBorder, deltaBorderWidthPx, deltaBorderBrush, showSpine, spineWidthPx, rightMarginPx, rightReservedPercent, xOffsetPx, showVolumeProfile, volumeLayer, volumeProfileWidthPx, volumeOpacity, volumeBrush, sideBySideGapPx);
		}

		public Indicators.LegToLegDeltaProfile_Opt LegToLegDeltaProfile_Opt(ISeries<double> input , ProfileModesOpt profileMode, SessionHoursOpt sessionHours, LegRotationModeOpt rotationMode, double rotationPoints, int maxProfileWidthPx, long minAbsDeltaToShow, int rebuildLookbackBarsCap, bool showProgressPrints, bool groupByZoom, int ticksPerLevel, bool autoDeltaText, int autoFontMin, int autoFontMax, int fontSize, int minRowHeightPx, WpfBrush positiveBrush, WpfBrush negativeBrush, WpfBrush textBrush, float deltaOpacity, bool showDeltaText, bool adaptiveDeltaText, bool showDeltaBorder, int deltaBorderWidthPx, WpfBrush deltaBorderBrush, bool showSpine, int spineWidthPx, int rightMarginPx, double rightReservedPercent, int xOffsetPx, bool showVolumeProfile, VolumeProfileLayerOpt volumeLayer, int volumeProfileWidthPx, float volumeOpacity, WpfBrush volumeBrush, int sideBySideGapPx)
		{
			return indicator.LegToLegDeltaProfile_Opt(input, profileMode, sessionHours, rotationMode, rotationPoints, maxProfileWidthPx, minAbsDeltaToShow, rebuildLookbackBarsCap, showProgressPrints, groupByZoom, ticksPerLevel, autoDeltaText, autoFontMin, autoFontMax, fontSize, minRowHeightPx, positiveBrush, negativeBrush, textBrush, deltaOpacity, showDeltaText, adaptiveDeltaText, showDeltaBorder, deltaBorderWidthPx, deltaBorderBrush, showSpine, spineWidthPx, rightMarginPx, rightReservedPercent, xOffsetPx, showVolumeProfile, volumeLayer, volumeProfileWidthPx, volumeOpacity, volumeBrush, sideBySideGapPx);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.LegToLegDeltaProfile_Opt LegToLegDeltaProfile_Opt(ProfileModesOpt profileMode, SessionHoursOpt sessionHours, LegRotationModeOpt rotationMode, double rotationPoints, int maxProfileWidthPx, long minAbsDeltaToShow, int rebuildLookbackBarsCap, bool showProgressPrints, bool groupByZoom, int ticksPerLevel, bool autoDeltaText, int autoFontMin, int autoFontMax, int fontSize, int minRowHeightPx, WpfBrush positiveBrush, WpfBrush negativeBrush, WpfBrush textBrush, float deltaOpacity, bool showDeltaText, bool adaptiveDeltaText, bool showDeltaBorder, int deltaBorderWidthPx, WpfBrush deltaBorderBrush, bool showSpine, int spineWidthPx, int rightMarginPx, double rightReservedPercent, int xOffsetPx, bool showVolumeProfile, VolumeProfileLayerOpt volumeLayer, int volumeProfileWidthPx, float volumeOpacity, WpfBrush volumeBrush, int sideBySideGapPx)
		{
			return indicator.LegToLegDeltaProfile_Opt(Input, profileMode, sessionHours, rotationMode, rotationPoints, maxProfileWidthPx, minAbsDeltaToShow, rebuildLookbackBarsCap, showProgressPrints, groupByZoom, ticksPerLevel, autoDeltaText, autoFontMin, autoFontMax, fontSize, minRowHeightPx, positiveBrush, negativeBrush, textBrush, deltaOpacity, showDeltaText, adaptiveDeltaText, showDeltaBorder, deltaBorderWidthPx, deltaBorderBrush, showSpine, spineWidthPx, rightMarginPx, rightReservedPercent, xOffsetPx, showVolumeProfile, volumeLayer, volumeProfileWidthPx, volumeOpacity, volumeBrush, sideBySideGapPx);
		}

		public Indicators.LegToLegDeltaProfile_Opt LegToLegDeltaProfile_Opt(ISeries<double> input , ProfileModesOpt profileMode, SessionHoursOpt sessionHours, LegRotationModeOpt rotationMode, double rotationPoints, int maxProfileWidthPx, long minAbsDeltaToShow, int rebuildLookbackBarsCap, bool showProgressPrints, bool groupByZoom, int ticksPerLevel, bool autoDeltaText, int autoFontMin, int autoFontMax, int fontSize, int minRowHeightPx, WpfBrush positiveBrush, WpfBrush negativeBrush, WpfBrush textBrush, float deltaOpacity, bool showDeltaText, bool adaptiveDeltaText, bool showDeltaBorder, int deltaBorderWidthPx, WpfBrush deltaBorderBrush, bool showSpine, int spineWidthPx, int rightMarginPx, double rightReservedPercent, int xOffsetPx, bool showVolumeProfile, VolumeProfileLayerOpt volumeLayer, int volumeProfileWidthPx, float volumeOpacity, WpfBrush volumeBrush, int sideBySideGapPx)
		{
			return indicator.LegToLegDeltaProfile_Opt(input, profileMode, sessionHours, rotationMode, rotationPoints, maxProfileWidthPx, minAbsDeltaToShow, rebuildLookbackBarsCap, showProgressPrints, groupByZoom, ticksPerLevel, autoDeltaText, autoFontMin, autoFontMax, fontSize, minRowHeightPx, positiveBrush, negativeBrush, textBrush, deltaOpacity, showDeltaText, adaptiveDeltaText, showDeltaBorder, deltaBorderWidthPx, deltaBorderBrush, showSpine, spineWidthPx, rightMarginPx, rightReservedPercent, xOffsetPx, showVolumeProfile, volumeLayer, volumeProfileWidthPx, volumeOpacity, volumeBrush, sideBySideGapPx);
		}
	}
}

#endregion
