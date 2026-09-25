#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript
{
    // Enums au namespace racine (et non .Indicators) : le bloc codegen NT8
    // ("NinjaScript generated code") les reference sans qualification depuis
    // les namespaces Indicators, MarketAnalyzerColumns et Strategies.
    public enum MgiAtrSessionMode { ETH, RTH }
    public enum MgiAtrAnchorSource { PriorDailyClose, SessionOpen }
    public enum MgiAtrLabelMode { Hidden, Name, Price, NameAndPrice }
    public enum MgiAtrDashStyle { Solid, Dash, Dot, DashDot, DashDotDot }
}

namespace NinjaTrader.NinjaScript.Indicators
{
    public class MGIATRLevels : Indicator
    {
        // Multiplicateurs fixes valides : R/S[i] = Base +/- ATR x Mults[i].
        private static readonly double[] Mults = new double[] { 0.5, 1.0, 2.0, 3.0 };

        private class AtrSession
        {
            public DateTime DayKey = DateTime.MinValue;
            public DateTime OpenDt = DateTime.MinValue;  // debut session (MinValue = fenetre encore inconnue, ex: overnight avant open RTH)
            public DateTime EndDt = DateTime.MaxValue;   // fin session (template)
            public int OpenBarIndex = -1;
            public int EndBarIndex = -1;
            public bool EndWideScanned;
            public bool EndSkipPrinted;
            public bool HasLevels;
            public bool OpenPxKnown;   // ancre SessionOpen capturee (1er open de session)
            public double OpenPx;
            public double Base;
            public double Atr;
            public double[] R = new double[4];
            public double[] S = new double[4];
        }

        private AtrSession _cur;
        private AtrSession _last;
        private Dictionary<DateTime, AtrSession> _hist;
        private DateTime _curDayKey = DateTime.MinValue;
        private bool _curRthStarted;

        private List<DateTime> _sortedCache = new List<DateTime>();
        private bool _sortedDirty = true;
        private HashSet<string> _drawnSuffixes;
        private string _lastDrawKey = null;

        // Sessions pilotees par les templates NT8 (aucune heure saisie) :
        // - ethIt : template du chart (convention CME : dimanche 18h ET = lundi).
        // - rthIt : serie secondaire Minute/1 (template RTH) -> fenetre [open, close) RTH.
        // - BIP1 : serie journaliere Day/1 (template selon SessionMode) -> prior close + ATR Wilder.
        private SessionIterator ethIt;
        private SessionIterator rthIt;
        private bool isIntradayPrimary = true;
        private DateTime rthOpenDt = DateTime.MinValue;
        private DateTime rthCloseDt = DateTime.MinValue;
        private DateTime rthWindowDay = DateTime.MinValue;
        private bool haveRthWindow;
        private bool needRthRefresh;
        private readonly Dictionary<DateTime, DateTime> rthOpenByDay = new Dictionary<DateTime, DateTime>();

        private string _priceFormat = "F2";
        private bool _warmPrinted;
        private bool _approxPrinted;
        // Prior close exact : capture au rollover daily (BIP1). A cet instant, Closes[1][1]
        // est la barre qui vient de se cloturer = J-1 final. Le primaire NE DOIT PAS lire
        // Closes[1][1] sur la 1re barre du jour (daily pas encore bascule : [1] = J-2).
        private double _priorDailyClose = double.NaN;
        private bool _priorDailyCloseKnown;
        private int _lastDailyIdx = -2;
        private int _dailyIdxAtSessionStart = -1;
        // Diagnostic "aucune session" : 1 print resume au lieu d'un echec silencieux.
        private bool _diagDone;
        private DateTime _rthWarnDay = DateTime.MinValue;

        public override string DisplayName => Name;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"MGI ATR Levels - S/R journaliers depuis Base (Prior Daily Close ou Session Open) +/- ATR Wilder daily x 0.5/1/2/3. Sessions via templates NT8 (ETH/RTH). Requiert donnees Day (+Minute/1 en RTH) ; ATR approxime si historique court. Lignes par session, historique N jours, Values pour backtest.";
                Name = "MGIATRLevels";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = true;
                PaintPriceMarkers = false;
                IsAutoScale = false;
                BarsRequiredToPlot = 2;
                SessionMode = MgiAtrSessionMode.ETH;
                AnchorSource = MgiAtrAnchorSource.PriorDailyClose;
                AtrPeriod = 20;
                MinAtrBars = 3;
                NumLevels = 4;
                ShowHistorical = true;
                HistoricalDays = 5;
                EthTemplate = "CME US Index Futures ETH";
                RthTemplate = "CME US Index Futures RTH";
                ShowResistance = true;
                RColor = Brushes.LimeGreen; RDash = MgiAtrDashStyle.Solid; RWidth = 2; RTextSize = 9;
                ShowSupport = true;
                SColor = Brushes.IndianRed; SDash = MgiAtrDashStyle.Solid; SWidth = 2; STextSize = 9;
                ShowBase = true;
                BaseColor = Brushes.White; BaseDash = MgiAtrDashStyle.Dash; BaseWidth = 2; BaseTextSize = 9;
                LabelMode = MgiAtrLabelMode.NameAndPrice;
                AddPlot(new Stroke(Brushes.White, 2), PlotStyle.Line, "Base");
                AddPlot(new Stroke(Brushes.LimeGreen, 2), PlotStyle.Line, "R1");
                AddPlot(new Stroke(Brushes.LimeGreen, 2), PlotStyle.Line, "R2");
                AddPlot(new Stroke(Brushes.LimeGreen, 2), PlotStyle.Line, "R3");
                AddPlot(new Stroke(Brushes.LimeGreen, 2), PlotStyle.Line, "R4");
                AddPlot(new Stroke(Brushes.IndianRed, 2), PlotStyle.Line, "S1");
                AddPlot(new Stroke(Brushes.IndianRed, 2), PlotStyle.Line, "S2");
                AddPlot(new Stroke(Brushes.IndianRed, 2), PlotStyle.Line, "S3");
                AddPlot(new Stroke(Brushes.IndianRed, 2), PlotStyle.Line, "S4");
                AddPlot(new Stroke(Brushes.Gray, 1), PlotStyle.Line, "ATR");
            }
            else if (State == State.Configure)
            {
                // Series secondaires : Day/1 (template selon SessionMode) pour prior close + ATR,
                // Minute/1 (template RTH) pour la fenetre RTH exacte. Inputs dispo en Configure
                // (re-instanciation a chaque changement de propriete) ; try/catch anti-crash.
                try
                {
                    string dayTH = SessionMode == MgiAtrSessionMode.RTH ? RthTemplate : EthTemplate;
                    if (string.IsNullOrEmpty(dayTH))
                        dayTH = SessionMode == MgiAtrSessionMode.RTH ? "CME US Index Futures RTH" : "CME US Index Futures ETH";
                    AddDataSeries(Instrument.FullName,
                        new BarsPeriod { BarsPeriodType = BarsPeriodType.Day, Value = 1 }, dayTH);
                }
                catch { }
                try
                {
                    string rthTH = string.IsNullOrEmpty(RthTemplate) ? "CME US Index Futures RTH" : RthTemplate;
                    AddDataSeries(Instrument.FullName,
                        new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = 1 }, rthTH);
                }
                catch { }
            }
            else if (State == State.DataLoaded)
            {
                _cur = null; _last = null;
                _hist = new Dictionary<DateTime, AtrSession>();
                _curDayKey = DateTime.MinValue; _curRthStarted = false;
                if (_sortedCache == null) _sortedCache = new List<DateTime>(); else _sortedCache.Clear();
                _sortedDirty = true;
                if (_drawnSuffixes == null) _drawnSuffixes = new HashSet<string>(); else _drawnSuffixes.Clear();
                _lastDrawKey = null;
                try { rthOpenByDay.Clear(); } catch { }
                rthOpenDt = DateTime.MinValue; rthCloseDt = DateTime.MinValue;
                rthWindowDay = DateTime.MinValue; haveRthWindow = false; needRthRefresh = false;
                _warmPrinted = false; _approxPrinted = false;
                _diagDone = false; _rthWarnDay = DateTime.MinValue; _lastAtrBarsUsed = 0;
                _priorDailyClose = double.NaN; _priorDailyCloseKnown = false;
                _lastDailyIdx = -2; _dailyIdxAtSessionStart = -1;
                try { ethIt = new SessionIterator(BarsArray[0]); } catch { ethIt = null; }
                try { rthIt = BarsArray.Length > 2 ? new SessionIterator(BarsArray[2]) : null; } catch { rthIt = null; }
                try
                {
                    BarsPeriodType bpt = BarsPeriod.BarsPeriodType;
                    isIntradayPrimary = bpt != BarsPeriodType.Day && bpt != BarsPeriodType.Week && bpt != BarsPeriodType.Month;
                    if (!isIntradayPrimary)
                        Print("[ATR] TF non-intraday : sessions desactivees (utiliser un chart intraday).");
                }
                catch { isIntradayPrimary = true; }
                // Format prix : decimales derivees du TickSize (ex: 0.25 -> F2).
                try
                {
                    double tick = Bars.Instrument.MasterInstrument.TickSize;
                    int d = 0; double t = tick;
                    while (d < 8 && Math.Abs(t - Math.Round(t)) > 1e-9) { t *= 10.0; d++; }
                    _priceFormat = "F" + d;
                }
                catch { _priceFormat = "F2"; }
                try { Print("ATR build 2026-09-21c (prior close au rollover daily) charge : " + Instrument.FullName + " " + BarsPeriod.Value + BarsPeriod.BarsPeriodType + " Mode=" + SessionMode + " Ancre=" + AnchorSource); } catch {}
                // Plots invisibles pour affichage (trace via Draw uniquement), Values gardees pour backtest.
                try { for (int i = 0; i < 10; i++) { Plots[i].Brush = Brushes.Transparent; Plots[i].Width = 1; } } catch {}
            }
            else if (State == State.Terminated)
            {
                _cur = null; _last = null;
                if (_hist != null) _hist.Clear();
                if (_sortedCache != null) _sortedCache.Clear(); _sortedDirty = true;
                if (_drawnSuffixes != null) _drawnSuffixes.Clear(); _lastDrawKey = null;
            }
        }

        #region Sessions / temps
        private DateTime SafeGetTradingDay(DateTime t)
        {
            try { if (ethIt != null) return ethIt.GetTradingDay(t).Date; } catch { }
            return t.Date;
        }

        private void TryRefreshRthWindow(DateTime dayKey, DateTime barOpenTime, DateTime barCloseTime)
        {
            if (rthIt == null || !isIntradayPrimary) return;
            try
            {
                rthIt.GetNextSession(barCloseTime, true);
                DateTime begin = rthIt.ActualSessionBegin;
                DateTime end = rthIt.ActualSessionEnd;
                if (begin != DateTime.MinValue && end != DateTime.MinValue
                    && barOpenTime >= begin && barOpenTime < end)
                {
                    rthOpenDt = begin; rthCloseDt = end;
                    rthWindowDay = dayKey; haveRthWindow = true; needRthRefresh = false;
                    return;
                }
                if (rthWindowDay != dayKey) haveRthWindow = false;
            }
            catch { }
        }

        private bool TryAdoptEthWindow(DateTime barOpenTime, DateTime barCloseTime, out DateTime begin, out DateTime end)
        {
            begin = DateTime.MinValue; end = DateTime.MaxValue;
            if (ethIt == null || !isIntradayPrimary) return false;
            try
            {
                ethIt.GetNextSession(barCloseTime, true);
                DateTime b = ethIt.ActualSessionBegin, e = ethIt.ActualSessionEnd;
                if (b != DateTime.MinValue && e != DateTime.MinValue && barOpenTime >= b && barOpenTime < e)
                { begin = b; end = e; return true; }
            }
            catch { }
            return false;
        }

        // CONVENTION CHART : l'heure affichee par chaque bougie = son CLOSE.
        private DateTime SubtractPeriod(DateTime close)
        {
            try
            {
                switch (BarsPeriod.BarsPeriodType)
                {
                    case BarsPeriodType.Minute: return close.AddMinutes(-BarsPeriod.Value);
                    case BarsPeriodType.Second: return close.AddSeconds(-BarsPeriod.Value);
                    case BarsPeriodType.Day: return close.Date;
                    case BarsPeriodType.Week: return close.Date;
                    default: return close;
                }
            }
            catch { return close; }
        }
        private int PCount()
        {
            try { return BarsArray[0].Count; } catch { try { return Count; } catch { return 0; } }
        }
        private int PCur()
        {
            try { return CurrentBars[0]; } catch { try { return CurrentBar; } catch { return 0; } }
        }
        private DateTime PTime(int barsAgo)
        {
            try { return Times[0][barsAgo]; } catch { try { return Time[barsAgo]; } catch { return DateTime.MinValue; } }
        }
        private DateTime GetBarOpenTime()
        {
            try { return SubtractPeriod(Times[0][0]); } catch { return DateTime.MinValue; }
        }
        private DateTime GetBarCloseTime()
        {
            try { return PTime(0); } catch { return DateTime.MinValue; }
        }
        private DateTime GetBarOpenTimeByBarsAgo(int barsAgo)
        {
            int pc = PCount();
            if (pc == 0) return DateTime.MinValue;
            if (barsAgo < 0) barsAgo = 0; if (barsAgo >= pc) barsAgo = pc - 1;
            try { return SubtractPeriod(PTime(barsAgo)); } catch { return DateTime.MinValue; }
        }
        private DateTime GetBarOpenTimeAbs(int absoluteIdx)
        {
            int pc = PCount(); int cur = PCur();
            if (pc == 0) return DateTime.MinValue;
            if (absoluteIdx < 0) absoluteIdx = 0; if (absoluteIdx >= pc) absoluteIdx = pc - 1;
            int barsAgo = cur - absoluteIdx;
            if (barsAgo < 0) barsAgo = 0; if (barsAgo >= pc) barsAgo = pc - 1;
            try { return SubtractPeriod(PTime(barsAgo)); } catch { return DateTime.MinValue; }
        }
        private int FindFirstBarAtOrAfter(DateTime time)
        {
            // PREMIERE bougie dont l'open >= time. Retourne un INDEX ABSOLU.
            try
            {
                int cur = PCur(); int pc = PCount();
                int maxB = Math.Min(cur, pc - 1);
                if (maxB < 0) return -1;
                int minB = Math.Max(0, maxB - 3000);
                for (int b = maxB; b >= minB; b--)
                {
                    DateTime o = GetBarOpenTimeByBarsAgo(b);
                    if (o == DateTime.MinValue) continue;
                    if (o >= time) return cur - b;
                }
            }
            catch { }
            return -1;
        }
        private int FindFirstBarAtOrAfterWide(DateTime time)
        {
            try
            {
                int cur = PCur(); int pc = PCount();
                int maxB = Math.Min(cur, pc - 1);
                if (maxB < 0) return -1;
                for (int b = maxB; b >= 0; b--)
                {
                    DateTime o = GetBarOpenTimeByBarsAgo(b);
                    if (o == DateTime.MinValue) continue;
                    if (o >= time) return cur - b;
                }
            }
            catch { }
            return -1;
        }
        private bool ValidStartIdx(int absIdx, DateTime openDt)
        {
            try
            {
                int cur = PCur();
                if (absIdx < 0 || absIdx > cur) return false;
                DateTime o = GetBarOpenTimeAbs(absIdx);
                if (o == DateTime.MinValue) return false;
                return o >= openDt && o < openDt.AddDays(7);
            }
            catch { return false; }
        }
        private int ResolveEndBarIndex(AtrSession lvl, DateTime endDt)
        {
            // INDEX ABSOLU : 1ere bougie ouvrant a/apres endDt, ou bougie courante si fin future.
            try
            {
                DateTime now;
                try { now = PTime(0); } catch { return -1; }
                if (endDt >= now) return PCur();
                int cur = PCur();
                if (lvl.EndBarIndex >= 0 && lvl.EndBarIndex <= cur)
                {
                    try
                    {
                        DateTime o = GetBarOpenTimeAbs(lvl.EndBarIndex);
                        if (o != DateTime.MinValue && o >= endDt && o < endDt.AddDays(4)) return lvl.EndBarIndex;
                    }
                    catch { }
                    lvl.EndBarIndex = -1; lvl.EndWideScanned = false;
                }
                int eIdx = FindFirstBarAtOrAfter(endDt);
                if (eIdx < 0 && !lvl.EndWideScanned)
                {
                    lvl.EndWideScanned = true;
                    eIdx = FindFirstBarAtOrAfterWide(endDt);
                }
                if (eIdx >= 0) { lvl.EndBarIndex = eIdx; return eIdx; }
                if (!lvl.EndSkipPrinted)
                {
                    lvl.EndSkipPrinted = true;
                    try { Print("ATR skip " + lvl.DayKey.ToString("yyyy-MM-dd") + " : fin introuvable (session non dessinee)"); } catch {}
                }
            }
            catch { }
            return -1;
        }
        private bool CheckDrawInvariant(int sBars, int eBars, int cur)
        {
            if (eBars >= 0 && sBars > eBars && sBars <= cur) return true;
            return false;
        }
        #endregion

        #region Calcul ATR / niveaux
        // ATR Wilder manuel sur la serie journaliere (BIP1), barres CLOTUREES uniquement :
        // TR(k) = max(H-L, |H-Cprev|, |L-Cprev|), seed = SMA des N plus anciens, puis RMA.
        // Aucun repaint : la valeur utilisee est figee au 1er calcul de la session.
        // Historique court : si moins de AtrPeriod TR dispo mais >= MinAtrBars, seed sur le
        // dispo (valeur approximative, _lastAtrBarsUsed < AtrPeriod) au lieu de ne rien tracer.
        private int _lastAtrBarsUsed;
        private double ComputeDailyAtr()
        {
            try
            {
                int period = Math.Max(1, AtrPeriod);
                int minBars = Math.Max(2, MinAtrBars);
                int cur;
                try { cur = CurrentBars[1]; } catch { _lastAtrBarsUsed = 0; return double.NaN; }
                int nTR = Math.Min(cur - 1, period + 2000);
                if (nTR < minBars) { _lastAtrBarsUsed = 0; return double.NaN; } // vrai warmup
                int eff = Math.Min(period, nTR);
                double sum = 0; int n = 0;
                // Seed : moyenne des `eff` TR les plus anciens (k=nTR..nTR-eff+1).
                for (int k = nTR; k >= nTR - eff + 1; k--)
                {
                    double h = Highs[1][k], l = Lows[1][k], c = Closes[1][k + 1];
                    if (double.IsNaN(h) || double.IsNaN(l) || double.IsNaN(c)) { _lastAtrBarsUsed = 0; return double.NaN; }
                    sum += Math.Max(h - l, Math.Max(Math.Abs(h - c), Math.Abs(l - c)));
                    n++;
                }
                if (n < eff) { _lastAtrBarsUsed = 0; return double.NaN; }
                double atr = sum / n;
                // RMA vers le plus recent (k=nTR-eff .. 1).
                for (int k = nTR - eff; k >= 1; k--)
                {
                    double h = Highs[1][k], l = Lows[1][k], c = Closes[1][k + 1];
                    if (double.IsNaN(h) || double.IsNaN(l) || double.IsNaN(c)) { _lastAtrBarsUsed = 0; return double.NaN; }
                    double tr = Math.Max(h - l, Math.Max(Math.Abs(h - c), Math.Abs(l - c)));
                    atr = (atr * (eff - 1) + tr) / eff;
                }
                _lastAtrBarsUsed = eff;
                return atr;
            }
            catch { _lastAtrBarsUsed = 0; return double.NaN; }
        }

        // Tente de figer les niveaux de _cur. Reussi quand ATR pret + base connue.
        private void TryComputeLevels()
        {
            try
            {
                if (_cur == null || _cur.HasLevels) return;
                if (CurrentBars[1] < 1) return;
                double atr = ComputeDailyAtr();
                if (double.IsNaN(atr) || double.IsInfinity(atr) || atr <= 0)
                {
                    if (!_warmPrinted)
                    {
                        _warmPrinted = true;
                        try { Print("[ATR] warmup : historique daily insuffisant (min " + Math.Max(2, MinAtrBars) + " barres Day requises, " + AtrPeriod + " pour valeur exacte). Telecharger les donnees Day."); } catch {}
                    }
                    return;
                }
                if (_lastAtrBarsUsed < Math.Max(1, AtrPeriod) && !_approxPrinted)
                {
                    _approxPrinted = true;
                    try { Print("[ATR] ATR approximatif : calcule sur " + _lastAtrBarsUsed + " barres daily au lieu de " + AtrPeriod + " (telecharger Day pour valeur exacte)."); } catch {}
                }
                double b = double.NaN;
                if (AnchorSource == MgiAtrAnchorSource.PriorDailyClose)
                {
                    // Attend le rollover daily observe APRES le debut de session : sur la 1re
                    // barre du jour le daily n'a pas encore bascule (Closes[1][1] = J-2).
                    if (!_priorDailyCloseKnown || _lastDailyIdx <= _dailyIdxAtSessionStart) return;
                    b = _priorDailyClose;
                }
                else if (_cur.OpenPxKnown)
                {
                    b = _cur.OpenPx;
                }
                else return;
                if (double.IsNaN(b) || double.IsInfinity(b)) return;
                _cur.Base = b; _cur.Atr = atr;
                for (int i = 0; i < 4; i++)
                {
                    _cur.R[i] = b + atr * Mults[i];
                    _cur.S[i] = b - atr * Mults[i];
                }
                _cur.HasLevels = true;
                _last = _cur;
                _hist[_cur.DayKey] = _cur;
                MarkSortedDirty();
            }
            catch { }
        }
        #endregion

        #region Dessin
        private void MarkSortedDirty() { _sortedDirty = true; }
        private List<DateTime> GetSortedSessions()
        {
            try
            {
                if (_sortedDirty || _sortedCache == null)
                {
                    if (_sortedCache == null) _sortedCache = new List<DateTime>();
                    _sortedCache.Clear();
                    if (_hist != null) { _sortedCache.AddRange(_hist.Keys); _sortedCache.Sort(); }
                    _sortedDirty = false;
                }
            }
            catch { }
            return _sortedCache;
        }

        private DashStyleHelper MapDash(MgiAtrDashStyle d)
        {
            switch (d)
            {
                case MgiAtrDashStyle.Dash: return DashStyleHelper.Dash;
                case MgiAtrDashStyle.Dot: return DashStyleHelper.Dot;
                case MgiAtrDashStyle.DashDot: return DashStyleHelper.DashDot;
                case MgiAtrDashStyle.DashDotDot: return DashStyleHelper.DashDotDot;
                default: return DashStyleHelper.Solid;
            }
        }

        private string BuildLabel(string name, double price)
        {
            try
            {
                switch (LabelMode)
                {
                    case MgiAtrLabelMode.Hidden: return string.Empty;
                    case MgiAtrLabelMode.Name: return name;
                    case MgiAtrLabelMode.Price: return price.ToString(_priceFormat);
                    default: return name + " " + price.ToString(_priceFormat);
                }
            }
            catch { return name; }
        }

        private void DrawAtrLevel(string tag, string name, int sBars, int eBars, double price, Brush brush, DashStyleHelper dash, int width, int textSize)
        {
            if (double.IsNaN(price) || double.IsInfinity(price)) { RemoveDrawObject(tag); RemoveDrawObject(tag + "_Lbl"); return; }
            if (sBars < eBars) { int t = sBars; sBars = eBars; eBars = t; }
            if (eBars < 0) eBars = 0;
            var line = Draw.Line(this, tag, sBars, price, eBars, price, brush);
            line.Stroke = new Stroke(brush, dash, width);
            string txt = BuildLabel(name, price);
            if (string.IsNullOrEmpty(txt)) { RemoveDrawObject(tag + "_Lbl"); return; }
            try { Draw.Text(this, tag + "_Lbl", false, txt, eBars, price, 5, brush, new Gui.Tools.SimpleFont("Arial", (float)textSize), TextAlignment.Left, null, null, 0); } catch {}
        }
        private void RemoveAtrLevel(string tag) { RemoveDrawObject(tag); RemoveDrawObject(tag + "_Lbl"); }
        private void RemoveSessionDraws(string suffix)
        {
            RemoveAtrLevel("ATR_Base_" + suffix);
            for (int i = 0; i < 4; i++)
            {
                RemoveAtrLevel("ATR_R" + (i + 1) + "_" + suffix);
                RemoveAtrLevel("ATR_S" + (i + 1) + "_" + suffix);
            }
        }
        #endregion

        protected override void OnBarUpdate()
        {
            try
            {
                // BIP2 (Minute/1 RTH) : signale chaque ouverture de session RTH.
                if (BarsInProgress == 2)
                {
                    try
                    {
                        if (SessionMode == MgiAtrSessionMode.RTH && CurrentBars[2] >= 0 && BarsArray[2].IsFirstBarOfSession)
                        {
                            int secVal = Math.Max(1, BarsArray[2].BarsPeriod.Value);
                            DateTime tO = Times[2][0].AddMinutes(-secVal);
                            DateTime dk = SafeGetTradingDay(tO);
                            if (!rthOpenByDay.ContainsKey(dk)) rthOpenByDay[dk] = tO;
                            if (rthOpenByDay.Count > 40)
                            {
                                List<DateTime> keys = new List<DateTime>(rthOpenByDay.Keys);
                                keys.Sort();
                                for (int i = 0; i < keys.Count - 20; i++) rthOpenByDay.Remove(keys[i]);
                            }
                            needRthRefresh = true;
                        }
                    }
                    catch { }
                    return;
                }
                if (BarsInProgress == 1)
                {
                    // Daily : capture du prior close a chaque rollover (nouvelle barre daily).
                    // Au moment du rollover, [1] = barre qui vient de se cloturer = J-1 final.
                    try
                    {
                        int dcur = CurrentBars[1];
                        if (dcur >= 1 && dcur != _lastDailyIdx)
                        {
                            _lastDailyIdx = dcur;
                            double c = Closes[1][1];
                            if (!double.IsNaN(c) && !double.IsInfinity(c))
                            { _priorDailyClose = c; _priorDailyCloseKnown = true; }
                        }
                    }
                    catch { }
                    return; // lecture passive pour le reste
                }
                if (BarsInProgress != 0) return;
                if (!isIntradayPrimary) return;
                if (CurrentBars[0] < 1 || CurrentBars[1] < 1) return;

                DateTime barOpenTime = GetBarOpenTime();
                DateTime barCloseTime = GetBarCloseTime();
                if (barOpenTime == DateTime.MinValue || barCloseTime == DateTime.MinValue) return;

                bool isNewEthSession = false;
                try { isNewEthSession = BarsArray[0].IsFirstBarOfSession; } catch { }
                DateTime dayKey = (isNewEthSession || _curDayKey == DateTime.MinValue)
                    ? SafeGetTradingDay(barOpenTime) : _curDayKey;
                bool newDay = dayKey != _curDayKey || _cur == null;

                // Diagnostic : si rien n'a jamais ete calcule apres 50 barres, resume la cause
                // probable en 1 print (daily vide ? RTH jamais adopte ?) au lieu du silence.
                if (!_diagDone && PCur() >= 50 && (_hist == null || _hist.Count == 0))
                {
                    _diagDone = true;
                    int dBars = -1;
                    try { dBars = CurrentBars[1]; } catch { }
                    try
                    {
                        Print("[ATR] diagnostic : aucune session calculee apres 50 barres (dailyBars=" + dBars
                            + ", RTHwindow=" + (SessionMode == MgiAtrSessionMode.RTH ? haveRthWindow.ToString() : "n/a")
                            + "). Verifier : download donnees Day" + (SessionMode == MgiAtrSessionMode.RTH ? " + Minute/1 RTH" : "")
                            + ", chart intraday, template chart coherent avec le mode.");
                    }
                    catch { }
                }

                if (newDay)
                {
                    if (_cur != null && _cur.HasLevels) { _last = _cur; _hist[_cur.DayKey] = _cur; MarkSortedDirty(); }
                    _curDayKey = dayKey;
                    _cur = new AtrSession { DayKey = dayKey, OpenBarIndex = PCur() };
                    _curRthStarted = false;
                    try { _dailyIdxAtSessionStart = CurrentBars[1]; } catch { _dailyIdxAtSessionStart = -1; }

                    if (SessionMode == MgiAtrSessionMode.ETH)
                    {
                        DateTime b, e;
                        if (TryAdoptEthWindow(barOpenTime, barCloseTime, out b, out e))
                        { _cur.OpenDt = b; _cur.EndDt = e; }
                        else
                        { _cur.OpenDt = barOpenTime; _cur.EndDt = DateTime.MaxValue; } // fallback : 1ere barre -> maintenant
                        if (AnchorSource == MgiAtrAnchorSource.SessionOpen)
                        { _cur.OpenPx = Opens[0][0]; _cur.OpenPxKnown = true; }
                    }
                    else
                    {
                        // RTH : adopte la fenetre du template si connue, sinon tentative immediate
                        // (reussit si le RTH est deja ouvert ; sinon overnight -> pending).
                        if (!(haveRthWindow && rthWindowDay == dayKey))
                        {
                            DateTime rthO;
                            if (rthOpenByDay.TryGetValue(dayKey, out rthO))
                                TryRefreshRthWindow(dayKey, rthO, rthO.AddMinutes(1));
                            else
                                TryRefreshRthWindow(dayKey, barOpenTime, barCloseTime);
                            needRthRefresh = false;
                        }
                        if (haveRthWindow && rthWindowDay == dayKey)
                        { _cur.OpenDt = rthOpenDt; _cur.EndDt = rthCloseDt; }
                        else
                        { _cur.OpenDt = DateTime.MinValue; _cur.EndDt = DateTime.MaxValue; }
                    }
                    TryComputeLevels();
                }
                else
                {
                    // Adoption RTH retardee (signalee par la serie secondaire, 1 appel/jour).
                    if (SessionMode == MgiAtrSessionMode.RTH && !haveRthWindow && rthWindowDay != dayKey && needRthRefresh)
                    {
                        DateTime rthO;
                        if (rthOpenByDay.TryGetValue(dayKey, out rthO))
                            TryRefreshRthWindow(dayKey, rthO, rthO.AddMinutes(1));
                        else
                            TryRefreshRthWindow(dayKey, barOpenTime, barCloseTime);
                        needRthRefresh = false;
                        if (haveRthWindow && rthWindowDay == dayKey && _cur != null)
                        { _cur.OpenDt = rthOpenDt; _cur.EndDt = rthCloseDt; }
                    }
                    // Capture de l'open RTH (ancre SessionOpen) a la 1ere barre RTH.
                    if (SessionMode == MgiAtrSessionMode.RTH && _cur != null && haveRthWindow && rthWindowDay == dayKey)
                    {
                        bool isRthBar = barOpenTime >= rthOpenDt && barOpenTime < rthCloseDt;
                        if (isRthBar && !_curRthStarted)
                        {
                            _curRthStarted = true;
                            if (AnchorSource == MgiAtrAnchorSource.SessionOpen && !_cur.OpenPxKnown)
                            { _cur.OpenPx = Opens[0][0]; _cur.OpenPxKnown = true; }
                        }
                    }
                    if (_cur != null && !_cur.HasLevels) TryComputeLevels();
                }

                // Values TOUJOURS renseignees (Show = affichage seul). NumLevels gate les extensions.
                AtrSession refLvl = null;
                if (_hist != null) _hist.TryGetValue(dayKey, out refLvl);
                if (refLvl == null || !refLvl.HasLevels)
                {
                    for (int i = 0; i < 10; i++) Values[i][0] = double.NaN;
                }
                else
                {
                    int n = Math.Max(1, Math.Min(4, NumLevels));
                    Values[0][0] = refLvl.Base;
                    for (int i = 0; i < 4; i++)
                    {
                        Values[1 + i][0] = i < n ? refLvl.R[i] : double.NaN;
                        Values[5 + i][0] = i < n ? refLvl.S[i] : double.NaN;
                    }
                    Values[9][0] = refLvl.Atr;
                }

                // Affichage : fenetre = HistoricalDays dernieres sessions (ShowHistorical) ou courante seule.
                int nHist = ShowHistorical ? Math.Max(1, HistoricalDays) : 1;
                List<DateTime> sorted = GetSortedSessions();
                if (sorted == null || sorted.Count == 0) return;
                int startIdx = Math.Max(0, sorted.Count - nHist);
                try
                {
                    string drawKey = nHist + "|" + startIdx + "|" + sorted.Count + "|" + sorted[sorted.Count - 1].Ticks;
                    if (drawKey != _lastDrawKey)
                    {
                        _lastDrawKey = drawKey;
                        HashSet<string> wanted = new HashSet<string>();
                        for (int w = startIdx; w < sorted.Count; w++) wanted.Add(sorted[w].ToString("yyyyMMdd"));
                        if (_drawnSuffixes != null)
                        {
                            foreach (string suf in new List<string>(_drawnSuffixes))
                                if (!wanted.Contains(suf)) { RemoveSessionDraws(suf); _drawnSuffixes.Remove(suf); }
                        }
                        if (_drawnSuffixes == null) _drawnSuffixes = new HashSet<string>();
                        foreach (string w in wanted) _drawnSuffixes.Add(w);
                    }
                }
                catch { }

                string baseName = AnchorSource == MgiAtrAnchorSource.PriorDailyClose ? "PDC" : "OPEN";
                int nD = Math.Max(1, Math.Min(4, NumLevels));
                DashStyleHelper rDash = MapDash(RDash), sDash = MapDash(SDash), bDash = MapDash(BaseDash);
                for (int s = startIdx; s < sorted.Count; s++)
                {
                    DateTime od = sorted[s];
                    AtrSession lvl;
                    try { lvl = _hist[od]; } catch { continue; }
                    if (lvl == null || !lvl.HasLevels) continue;
                    if (lvl.OpenDt == DateTime.MinValue)
                    {
                        // Niveaux calcules mais fenetre de session inconnue (RTH non adopte) :
                        // session non dessinable -> 1 print/jour au lieu du silence.
                        if (SessionMode == MgiAtrSessionMode.RTH && _rthWarnDay != od)
                        {
                            _rthWarnDay = od;
                            try { Print("[ATR] fenetre RTH inconnue pour " + od.ToString("yyyy-MM-dd") + " (donnees Minute/1 template RTH manquantes ?)."); } catch {}
                        }
                        continue;
                    }
                    DateTime eDt;
                    try
                    {
                        DateTime nextOpen = (s + 1 < sorted.Count) ? _hist[sorted[s + 1]].OpenDt : DateTime.MaxValue;
                        eDt = lvl.EndDt;
                        if (nextOpen != DateTime.MinValue && nextOpen < eDt) eDt = nextOpen;
                        if (s == sorted.Count - 1 && PTime(0) < eDt) eDt = PTime(0);
                    }
                    catch { eDt = lvl.EndDt; }
                    int curN = PCur();
                    int sIdx = ValidStartIdx(lvl.OpenBarIndex, lvl.OpenDt) ? lvl.OpenBarIndex : FindFirstBarAtOrAfter(lvl.OpenDt);
                    if (sIdx < 0 || sIdx > curN || !ValidStartIdx(sIdx, lvl.OpenDt)) continue;
                    int eIdx = ResolveEndBarIndex(lvl, eDt);
                    if (eIdx < 0) continue;
                    int barsAgoStart = curN - sIdx;
                    int barsAgoEnd = curN - eIdx;
                    if (!CheckDrawInvariant(barsAgoStart, barsAgoEnd, curN)) continue;
                    string suffix = od.ToString("yyyyMMdd");
                    if (ShowBase)
                        DrawAtrLevel("ATR_Base_" + suffix, baseName, barsAgoStart, barsAgoEnd, lvl.Base, BaseColor, bDash, BaseWidth, BaseTextSize);
                    else RemoveAtrLevel("ATR_Base_" + suffix);
                    if (ShowResistance)
                    {
                        for (int i = 0; i < nD; i++)
                            DrawAtrLevel("ATR_R" + (i + 1) + "_" + suffix, "R" + (i + 1), barsAgoStart, barsAgoEnd, lvl.R[i], RColor, rDash, RWidth, RTextSize);
                        for (int i = nD; i < 4; i++) RemoveAtrLevel("ATR_R" + (i + 1) + "_" + suffix);
                    }
                    else { for (int i = 0; i < 4; i++) RemoveAtrLevel("ATR_R" + (i + 1) + "_" + suffix); }
                    if (ShowSupport)
                    {
                        for (int i = 0; i < nD; i++)
                            DrawAtrLevel("ATR_S" + (i + 1) + "_" + suffix, "S" + (i + 1), barsAgoStart, barsAgoEnd, lvl.S[i], SColor, sDash, SWidth, STextSize);
                        for (int i = nD; i < 4; i++) RemoveAtrLevel("ATR_S" + (i + 1) + "_" + suffix);
                    }
                    else { for (int i = 0; i < 4; i++) RemoveAtrLevel("ATR_S" + (i + 1) + "_" + suffix); }
                }
            }
            catch (Exception ex) { try { Print("ATR " + ex.Message); } catch {} }
        }

        #region Accessors
        [Browsable(false)][XmlIgnore] public double Base => Values[0][0];
        [Browsable(false)][XmlIgnore] public double R1 => Values[1][0];
        [Browsable(false)][XmlIgnore] public double R2 => Values[2][0];
        [Browsable(false)][XmlIgnore] public double R3 => Values[3][0];
        [Browsable(false)][XmlIgnore] public double R4 => Values[4][0];
        [Browsable(false)][XmlIgnore] public double S1 => Values[5][0];
        [Browsable(false)][XmlIgnore] public double S2 => Values[6][0];
        [Browsable(false)][XmlIgnore] public double S3 => Values[7][0];
        [Browsable(false)][XmlIgnore] public double S4 => Values[8][0];
        [Browsable(false)][XmlIgnore] public double AtrUsed => Values[9][0];
        #endregion

        #region Properties - Session
        [NinjaScriptProperty]
        [Display(Name = "Mode session (template)", Order = 1, GroupName = "01 - Session", Description = "ETH = journee complete (template ETH), RTH = session cash (template RTH). Change aussi le template de la serie journaliere.")]
        public MgiAtrSessionMode SessionMode { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Ancre des niveaux", Order = 2, GroupName = "01 - Session", Description = "Base = PriorDailyClose (cloture daily precedente) ou SessionOpen (open de la session).")]
        public MgiAtrAnchorSource AnchorSource { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Periode ATR daily", Order = 3, GroupName = "01 - Session")]
        [Range(1, 100)] public int AtrPeriod { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Min barres daily (approx)", Order = 4, GroupName = "01 - Session", Description = "Sous ce nombre de barres Day : aucun calcul (warmup). Entre ce minimum et Periode ATR : ATR approxime sur le dispo + warning.")]
        [Range(2, 100)] public int MinAtrBars { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Nombre de paires S/R", Order = 5, GroupName = "01 - Session", Description = "1 = R1/S1 seuls ... 4 = R1-R4/S1-S4.")]
        [Range(1, 4)] public int NumLevels { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Afficher l'historique", Order = 6, GroupName = "01 - Session")]
        public bool ShowHistorical { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Jours d'historique", Order = 7, GroupName = "01 - Session")]
        [Range(1, 100)] public int HistoricalDays { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Template ETH (daily)", Order = 8, GroupName = "01 - Session")]
        public string EthTemplate { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Template RTH (daily+RTH)", Order = 9, GroupName = "01 - Session")]
        public string RthTemplate { get; set; }
        #endregion

        #region Properties - Resistances
        [NinjaScriptProperty]
        [Display(Name = "Afficher resistances", Order = 1, GroupName = "02 - Resistances")]
        public bool ShowResistance { get; set; }
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Couleur R", Order = 2, GroupName = "02 - Resistances")]
        public Brush RColor { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Style ligne R", Order = 3, GroupName = "02 - Resistances")]
        public MgiAtrDashStyle RDash { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Epaisseur ligne R", Order = 4, GroupName = "02 - Resistances")]
        [Range(1, 5)] public int RWidth { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Taille texte R", Order = 5, GroupName = "02 - Resistances")]
        [Range(7, 16)] public int RTextSize { get; set; }
        #endregion

        #region Properties - Supports
        [NinjaScriptProperty]
        [Display(Name = "Afficher supports", Order = 1, GroupName = "03 - Supports")]
        public bool ShowSupport { get; set; }
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Couleur S", Order = 2, GroupName = "03 - Supports")]
        public Brush SColor { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Style ligne S", Order = 3, GroupName = "03 - Supports")]
        public MgiAtrDashStyle SDash { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Epaisseur ligne S", Order = 4, GroupName = "03 - Supports")]
        [Range(1, 5)] public int SWidth { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Taille texte S", Order = 5, GroupName = "03 - Supports")]
        [Range(7, 16)] public int STextSize { get; set; }
        #endregion

        #region Properties - Base
        [NinjaScriptProperty]
        [Display(Name = "Afficher base (PDC/OPEN)", Order = 1, GroupName = "04 - Base")]
        public bool ShowBase { get; set; }
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Couleur base", Order = 2, GroupName = "04 - Base")]
        public Brush BaseColor { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Style ligne base", Order = 3, GroupName = "04 - Base")]
        public MgiAtrDashStyle BaseDash { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Epaisseur ligne base", Order = 4, GroupName = "04 - Base")]
        [Range(1, 5)] public int BaseWidth { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Taille texte base", Order = 5, GroupName = "04 - Base")]
        [Range(7, 16)] public int BaseTextSize { get; set; }
        #endregion

        #region Properties - Labels
        [NinjaScriptProperty]
        [Display(Name = "Contenu des labels", Order = 1, GroupName = "05 - Labels", Description = "Hidden = lignes seules, Name = R1/S1/PDC, Price = prix seul, NameAndPrice = les deux.")]
        public MgiAtrLabelMode LabelMode { get; set; }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MGIATRLevels[] cacheMGIATRLevels;
		public MGIATRLevels MGIATRLevels(MgiAtrSessionMode sessionMode, MgiAtrAnchorSource anchorSource, int atrPeriod, int minAtrBars, int numLevels, bool showHistorical, int historicalDays, string ethTemplate, string rthTemplate, bool showResistance, Brush rColor, MgiAtrDashStyle rDash, int rWidth, int rTextSize, bool showSupport, Brush sColor, MgiAtrDashStyle sDash, int sWidth, int sTextSize, bool showBase, Brush baseColor, MgiAtrDashStyle baseDash, int baseWidth, int baseTextSize, MgiAtrLabelMode labelMode)
		{
			return MGIATRLevels(Input, sessionMode, anchorSource, atrPeriod, minAtrBars, numLevels, showHistorical, historicalDays, ethTemplate, rthTemplate, showResistance, rColor, rDash, rWidth, rTextSize, showSupport, sColor, sDash, sWidth, sTextSize, showBase, baseColor, baseDash, baseWidth, baseTextSize, labelMode);
		}

		public MGIATRLevels MGIATRLevels(ISeries<double> input, MgiAtrSessionMode sessionMode, MgiAtrAnchorSource anchorSource, int atrPeriod, int minAtrBars, int numLevels, bool showHistorical, int historicalDays, string ethTemplate, string rthTemplate, bool showResistance, Brush rColor, MgiAtrDashStyle rDash, int rWidth, int rTextSize, bool showSupport, Brush sColor, MgiAtrDashStyle sDash, int sWidth, int sTextSize, bool showBase, Brush baseColor, MgiAtrDashStyle baseDash, int baseWidth, int baseTextSize, MgiAtrLabelMode labelMode)
		{
			if (cacheMGIATRLevels != null)
				for (int idx = 0; idx < cacheMGIATRLevels.Length; idx++)
					if (cacheMGIATRLevels[idx] != null && cacheMGIATRLevels[idx].SessionMode == sessionMode && cacheMGIATRLevels[idx].AnchorSource == anchorSource && cacheMGIATRLevels[idx].AtrPeriod == atrPeriod && cacheMGIATRLevels[idx].MinAtrBars == minAtrBars && cacheMGIATRLevels[idx].NumLevels == numLevels && cacheMGIATRLevels[idx].ShowHistorical == showHistorical && cacheMGIATRLevels[idx].HistoricalDays == historicalDays && cacheMGIATRLevels[idx].EthTemplate == ethTemplate && cacheMGIATRLevels[idx].RthTemplate == rthTemplate && cacheMGIATRLevels[idx].ShowResistance == showResistance && cacheMGIATRLevels[idx].RColor == rColor && cacheMGIATRLevels[idx].RDash == rDash && cacheMGIATRLevels[idx].RWidth == rWidth && cacheMGIATRLevels[idx].RTextSize == rTextSize && cacheMGIATRLevels[idx].ShowSupport == showSupport && cacheMGIATRLevels[idx].SColor == sColor && cacheMGIATRLevels[idx].SDash == sDash && cacheMGIATRLevels[idx].SWidth == sWidth && cacheMGIATRLevels[idx].STextSize == sTextSize && cacheMGIATRLevels[idx].ShowBase == showBase && cacheMGIATRLevels[idx].BaseColor == baseColor && cacheMGIATRLevels[idx].BaseDash == baseDash && cacheMGIATRLevels[idx].BaseWidth == baseWidth && cacheMGIATRLevels[idx].BaseTextSize == baseTextSize && cacheMGIATRLevels[idx].LabelMode == labelMode && cacheMGIATRLevels[idx].EqualsInput(input))
						return cacheMGIATRLevels[idx];
			return CacheIndicator<MGIATRLevels>(new MGIATRLevels(){ SessionMode = sessionMode, AnchorSource = anchorSource, AtrPeriod = atrPeriod, MinAtrBars = minAtrBars, NumLevels = numLevels, ShowHistorical = showHistorical, HistoricalDays = historicalDays, EthTemplate = ethTemplate, RthTemplate = rthTemplate, ShowResistance = showResistance, RColor = rColor, RDash = rDash, RWidth = rWidth, RTextSize = rTextSize, ShowSupport = showSupport, SColor = sColor, SDash = sDash, SWidth = sWidth, STextSize = sTextSize, ShowBase = showBase, BaseColor = baseColor, BaseDash = baseDash, BaseWidth = baseWidth, BaseTextSize = baseTextSize, LabelMode = labelMode }, input, ref cacheMGIATRLevels);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MGIATRLevels MGIATRLevels(MgiAtrSessionMode sessionMode, MgiAtrAnchorSource anchorSource, int atrPeriod, int minAtrBars, int numLevels, bool showHistorical, int historicalDays, string ethTemplate, string rthTemplate, bool showResistance, Brush rColor, MgiAtrDashStyle rDash, int rWidth, int rTextSize, bool showSupport, Brush sColor, MgiAtrDashStyle sDash, int sWidth, int sTextSize, bool showBase, Brush baseColor, MgiAtrDashStyle baseDash, int baseWidth, int baseTextSize, MgiAtrLabelMode labelMode)
		{
			return indicator.MGIATRLevels(Input, sessionMode, anchorSource, atrPeriod, minAtrBars, numLevels, showHistorical, historicalDays, ethTemplate, rthTemplate, showResistance, rColor, rDash, rWidth, rTextSize, showSupport, sColor, sDash, sWidth, sTextSize, showBase, baseColor, baseDash, baseWidth, baseTextSize, labelMode);
		}

		public Indicators.MGIATRLevels MGIATRLevels(ISeries<double> input , MgiAtrSessionMode sessionMode, MgiAtrAnchorSource anchorSource, int atrPeriod, int minAtrBars, int numLevels, bool showHistorical, int historicalDays, string ethTemplate, string rthTemplate, bool showResistance, Brush rColor, MgiAtrDashStyle rDash, int rWidth, int rTextSize, bool showSupport, Brush sColor, MgiAtrDashStyle sDash, int sWidth, int sTextSize, bool showBase, Brush baseColor, MgiAtrDashStyle baseDash, int baseWidth, int baseTextSize, MgiAtrLabelMode labelMode)
		{
			return indicator.MGIATRLevels(input, sessionMode, anchorSource, atrPeriod, minAtrBars, numLevels, showHistorical, historicalDays, ethTemplate, rthTemplate, showResistance, rColor, rDash, rWidth, rTextSize, showSupport, sColor, sDash, sWidth, sTextSize, showBase, baseColor, baseDash, baseWidth, baseTextSize, labelMode);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MGIATRLevels MGIATRLevels(MgiAtrSessionMode sessionMode, MgiAtrAnchorSource anchorSource, int atrPeriod, int minAtrBars, int numLevels, bool showHistorical, int historicalDays, string ethTemplate, string rthTemplate, bool showResistance, Brush rColor, MgiAtrDashStyle rDash, int rWidth, int rTextSize, bool showSupport, Brush sColor, MgiAtrDashStyle sDash, int sWidth, int sTextSize, bool showBase, Brush baseColor, MgiAtrDashStyle baseDash, int baseWidth, int baseTextSize, MgiAtrLabelMode labelMode)
		{
			return indicator.MGIATRLevels(Input, sessionMode, anchorSource, atrPeriod, minAtrBars, numLevels, showHistorical, historicalDays, ethTemplate, rthTemplate, showResistance, rColor, rDash, rWidth, rTextSize, showSupport, sColor, sDash, sWidth, sTextSize, showBase, baseColor, baseDash, baseWidth, baseTextSize, labelMode);
		}

		public Indicators.MGIATRLevels MGIATRLevels(ISeries<double> input , MgiAtrSessionMode sessionMode, MgiAtrAnchorSource anchorSource, int atrPeriod, int minAtrBars, int numLevels, bool showHistorical, int historicalDays, string ethTemplate, string rthTemplate, bool showResistance, Brush rColor, MgiAtrDashStyle rDash, int rWidth, int rTextSize, bool showSupport, Brush sColor, MgiAtrDashStyle sDash, int sWidth, int sTextSize, bool showBase, Brush baseColor, MgiAtrDashStyle baseDash, int baseWidth, int baseTextSize, MgiAtrLabelMode labelMode)
		{
			return indicator.MGIATRLevels(input, sessionMode, anchorSource, atrPeriod, minAtrBars, numLevels, showHistorical, historicalDays, ethTemplate, rthTemplate, showResistance, rColor, rDash, rWidth, rTextSize, showSupport, sColor, sDash, sWidth, sTextSize, showBase, baseColor, baseDash, baseWidth, baseTextSize, labelMode);
		}
	}
}

#endregion
