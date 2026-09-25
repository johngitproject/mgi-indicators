#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class MGIPriorDayOHLC_Opt : Indicator
    {
        private double priorDayHigh;
        private double priorDayLow;
        private double priorDayOpen;
        private double priorDayClose;
        private double currentDayHigh;
        private double currentDayLow;
        private double currentDayOpen;
        private bool hasPriorDay;

        private DateTime currentWeekKey;
        private double priorWeekHigh;
        private double priorWeekLow;
        private double priorWeekOpen;
        private double priorWeekClose;
        private double currentWeekHigh;
        private double currentWeekLow;
        private double currentWeekOpen;
        private bool hasCurrentWeek;
        private bool hasPriorWeek;

private DateTime currentMonthKey;
        private double priorMonthHigh;
        private double priorMonthLow;
        private double priorMonthOpen;
        private double priorMonthClose;
        private double currentMonthHigh;
        private double currentMonthLow;
        private double currentMonthOpen;
        private bool hasCurrentMonth;
        private bool hasPriorMonth;

        private double overnightHigh;
        private double overnightLow;
        private bool hasOvernight;
        private bool overnightFinalized;

        private bool rthStarted;
        private double rthOpen;

        private bool orActive;
        private bool orFinalized;
        private double orHigh;
        private double orLow;

        private bool ibActive;
        private bool ibFinalized;
        private double ibHigh;
        private double ibLow;

        private int currentDayStartBar = -1;
        private int currentWeekStartBar = -1;
        private int currentMonthStartBar = -1;
        private int rthStartBar = -1;
        private int orEndBar = -1;
        private int ibEndBar = -1;
        private DateTime currentDayKey;

        // Volume Profile (VAH / VAL / VPOC)
        private Dictionary<long, double> currentDayProfile;
        private Dictionary<long, double> currentWeekProfile;
        private Dictionary<long, double> currentMonthProfile;
        private Dictionary<long, double> currentOvernightProfile;
        private double profileStep = 1.0;
        private double priorDayVAH;
        private double priorDayVAL;
        private double priorDayVPOC;
        private bool hasPriorDayProfile;
        private double priorWeekVAH;
        private double priorWeekVAL;
        private double priorWeekVPOC;
        private bool hasPriorWeekProfile;
        private double priorMonthVAH;
        private double priorMonthVAL;
        private double priorMonthVPOC;
        private bool hasPriorMonthProfile;
        private double overnightVAH;
        private double overnightVAL;
        private double overnightVPOC;
        private bool hasOvernightProfile;

        // VAH / VAL / VPOC LIVE de la période en cours (fixe + developing)
        private double currentDayVAH;
        private double currentDayVAL;
        private double currentDayVPOC;
        private double currentWeekVAH;
        private double currentWeekVAL;
        private double currentWeekVPOC;
        private double currentMonthVAH;
        private double currentMonthVAL;
        private double currentMonthVPOC;

        // VWAP cumulés (jour / semaine / mois en cours)
        private double dayVwapSumPV;
        private double dayVwapSumV;
        private double dayVWAP;
        private double weekVwapSumPV;
        private double weekVwapSumV;
        private double weekVWAP;
        private double monthVwapSumPV;
        private double monthVwapSumV;
        private double monthVWAP;

        // Heures effectives des sessions (saisies manuellement) : début
        // overnight, ouverture RTH. Le reste du code ne travaille qu'avec
        // ces valeurs pour rester cohérent avec les barres du chart.
        private TimeSpan effOvernightStart;
        private TimeSpan effRTHOpen;

        // Cache des niveaux triés par profil : on ne re-trie que lorsque de
        // nouveaux niveaux de prix apparaissent (le volume ajouté ne change
        // pas la liste des clés). Clé = référence du profil (égalité de référence).
        private Dictionary<Dictionary<long, double>, List<long>> sortedLevelCache;

        // P0-opt : GetBarOpenTime() (Time[0]-periode + try/catch) etait appele
        // jusqu'a 8x par barre via GetBarOpenTimeOfDay (4 profils x2). Cache 1x/barre.
        private int _accCacheBar = -1;
        private TimeSpan _accCacheTOD;

        // Feedback + diagnostic chargement (ShowProgressPrints) : Prints throttles
        // dans Output + chrono total/accum/VA. Surcout nul quand OFF ou temps reel.
        private bool _progStarted = false;
        private int _progCount = 0;
        private int _progNext = 0;
        private DateTime _progDoneAt = DateTime.MinValue;
        private System.Diagnostics.Stopwatch _swTotal = new System.Diagnostics.Stopwatch();
        private System.Diagnostics.Stopwatch _swBody = new System.Diagnostics.Stopwatch();
        private System.Diagnostics.Stopwatch _swAccum = new System.Diagnostics.Stopwatch();
        private System.Diagnostics.Stopwatch _swVA = new System.Diagnostics.Stopwatch();

        public override string DisplayName
        {
            get { return Name; }
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"MGI - Prior Day / Week / Month High, Low, Open, Close. Affiche les niveaux des périodes précédentes.";
                Name = "MGI Prior Day Week Month OHLC Opt";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = false;
                DrawVerticalGridLines = false;
                PaintPriceMarkers = false;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

ShowDay = true;
                ShowDayPriorVA = true;
                ShowCurrentDay = true;
                ShowDayCurrentVA = true;
                ShowDayVWAP = true;

                ShowWeek = true;
                ShowWeekPriorVA = true;
                ShowCurrentWeek = true;
                ShowWeekCurrentVA = true;
                ShowWeekVWAP = true;

                ShowMonth = true;
                ShowMonthPriorVA = true;
                ShowCurrentMonth = true;
                ShowMonthCurrentVA = true;
                ShowMonthVWAP = true;

                ShowOvernight = true;
                ShowOvernightProfile = true;
                OvernightStartTime = new TimeSpan(18, 0, 0);
                OvernightRTHOpenTime = new TimeSpan(14, 30, 0);

                ShowRTH = true;
                OpeningRangeMinutes = 5;
                ShowOpeningRange = true;

                ShowInitialBalance = true;
                IBStartTime = new TimeSpan(14, 30, 0);
                IBEndTime = new TimeSpan(15, 30, 0);

                ValueAreaPercent = 70.0;
                ProfileUseRTHOnly = true;
                TicksPerLevel = 4;

                LabelOffsetX = 8.0;
                LabelOffsetY = 0.0;
                LabelFontSize = 11.0;

                HideBars = false;
                ShowProgressPrints = true;

                DailyLineWidth = 2;
                WeeklyLineWidth = 2;
                MonthlyLineWidth = 2;
                OvernightLineWidth = 2;
                RTHLineWidth = 2;
                ORLineWidth = 2;
                IBLineWidth = 2;

AddPlot(new Stroke(Brushes.Red, 2), PlotStyle.Line, "PriorDayHigh");
                AddPlot(new Stroke(Brushes.Red, 2), PlotStyle.Line, "PriorDayLow");
                AddPlot(new Stroke(Brushes.Red, 2), PlotStyle.Line, "PriorDayOpen");
                AddPlot(new Stroke(Brushes.Red, 2), PlotStyle.Line, "PriorDayClose");

                AddPlot(new Stroke(Brushes.DarkRed, 2), PlotStyle.Line, "PriorWeekHigh");
                AddPlot(new Stroke(Brushes.DarkRed, 2), PlotStyle.Line, "PriorWeekLow");
                AddPlot(new Stroke(Brushes.DarkRed, 2), PlotStyle.Line, "PriorWeekOpen");
                AddPlot(new Stroke(Brushes.DarkRed, 2), PlotStyle.Line, "PriorWeekClose");

                AddPlot(new Stroke(Brushes.Magenta, 2), PlotStyle.Line, "PriorMonthHigh");
                AddPlot(new Stroke(Brushes.Magenta, 2), PlotStyle.Line, "PriorMonthLow");
                AddPlot(new Stroke(Brushes.Magenta, 2), PlotStyle.Line, "PriorMonthOpen");
                AddPlot(new Stroke(Brushes.Magenta, 2), PlotStyle.Line, "PriorMonthClose");

                AddPlot(new Stroke(Brushes.Red, 1), PlotStyle.Dot, "CurrentDayHigh");
                AddPlot(new Stroke(Brushes.Red, 1), PlotStyle.Dot, "CurrentDayLow");
                AddPlot(new Stroke(Brushes.Red, 1), PlotStyle.Dot, "CurrentDayOpen");

                AddPlot(new Stroke(Brushes.DarkRed, 1), PlotStyle.Dot, "CurrentWeekHigh");
                AddPlot(new Stroke(Brushes.DarkRed, 1), PlotStyle.Dot, "CurrentWeekLow");
                AddPlot(new Stroke(Brushes.DarkRed, 1), PlotStyle.Dot, "CurrentWeekOpen");

                AddPlot(new Stroke(Brushes.Magenta, 1), PlotStyle.Dot, "CurrentMonthHigh");
                AddPlot(new Stroke(Brushes.Magenta, 1), PlotStyle.Dot, "CurrentMonthLow");
                AddPlot(new Stroke(Brushes.Magenta, 1), PlotStyle.Dot, "CurrentMonthOpen");

                AddPlot(new Stroke(Brushes.DeepPink, 1), PlotStyle.Dot, "OvernightHigh");
                AddPlot(new Stroke(Brushes.DeepPink, 1), PlotStyle.Dot, "OvernightLow");

                AddPlot(new Stroke(Brushes.SteelBlue, 2), PlotStyle.Line, "RTHOpen");

                AddPlot(new Stroke(Brushes.OrangeRed, 2), PlotStyle.Line, "OpeningRangeHigh");
                AddPlot(new Stroke(Brushes.OrangeRed, 2), PlotStyle.Line, "OpeningRangeLow");
                AddPlot(new Stroke(Brushes.OrangeRed, 1), PlotStyle.Line, "OpeningRangeMid");

                AddPlot(new Stroke(Brushes.BlueViolet, 2), PlotStyle.Line, "InitialBalanceHigh");
                AddPlot(new Stroke(Brushes.BlueViolet, 2), PlotStyle.Line, "InitialBalanceLow");
                AddPlot(new Stroke(Brushes.BlueViolet, 1), PlotStyle.Line, "InitialBalanceMid");

                // VWAP cumulés (traces)
                AddPlot(new Stroke(Brushes.Cyan, 1), PlotStyle.Line, "DayVWAP");
                AddPlot(new Stroke(Brushes.Cyan, 1), PlotStyle.Line, "WeekVWAP");
                AddPlot(new Stroke(Brushes.Cyan, 1), PlotStyle.Line, "MonthVWAP");

DailyColor = Brushes.Red;
                WeeklyColor = Brushes.DarkRed;
                MonthlyColor = Brushes.Magenta;
                OvernightColor = Brushes.DeepPink;
                ORColor = Brushes.OrangeRed;
                IBColor = Brushes.BlueViolet;
                RTHColor = Brushes.SteelBlue;
            }
            else if (State == State.Terminated)
            {
                // nothing to clean up
            }
            else if (State == State.DataLoaded)
            {
                // Pas du profil : un level = TicksPerLevel ticks. Regrouper les
                // niveaux par ticks rend le calcul plus rapide et le profil plus
                // condensé (moins de niveaux dans le dictionnaire).
                // On force au minimum 1 tick/level (évite les erreurs si un
                // ancien template charge une valeur à 0).
                profileStep = Bars.Instrument.MasterInstrument.TickSize * Math.Max(1, TicksPerLevel);
                if (profileStep <= 0)
                    profileStep = 1.0;

                // Horaires de session : saisie manuelle par l'utilisateur.
                // Ces heures servent de référence à toute la logique (overnight,
                // RTH, Opening Range, Initial Balance, profils).
                effOvernightStart = OvernightStartTime;
                effRTHOpen = OvernightRTHOpenTime;
                _accCacheBar = -1;
                _cachedLabelBar = -1;
                _cachedLabelTime = DateTime.MinValue;
                _progStarted = false;
                _progCount = 0;
                _progNext = 0;
                _progDoneAt = DateTime.MinValue;
                currentDayProfile = new Dictionary<long, double>();
                currentWeekProfile = new Dictionary<long, double>();
                currentMonthProfile = new Dictionary<long, double>();
                currentOvernightProfile = new Dictionary<long, double>();

                // Les VWAP portent la couleur et l'épaisseur de leur catégorie.
                Plots[30].Brush = DailyColor;
                Plots[30].Width = DailyLineWidth;
                Plots[31].Brush = WeeklyColor;
                Plots[31].Width = WeeklyLineWidth;
                Plots[32].Brush = MonthlyColor;
                Plots[32].Width = MonthlyLineWidth;
            }
            else if (State == State.Terminated)
            {
                // Restaure les bougies si elles avaient été cachées
                BarBrushes[0] = null;
                CandleOutlineBrushes[0] = null;
            }
            if (State == State.Realtime && ShowProgressPrints && _progDoneAt != DateTime.MinValue)
            {
                try { Print(string.Format("[OPT] temps reel atteint, transition={0:F1}s apres fin historique", (DateTime.Now - _progDoneAt).TotalSeconds)); } catch {}
                _progDoneAt = DateTime.MinValue;
            }
        }

        private DateTime GetWeekKey(DateTime time)
        {
            DateTime date = time.Date;
            int dow = (int)date.DayOfWeek; // 0 = dimanche
            int daysSinceMonday = dow == 0 ? 6 : dow - 1;
            return date.AddDays(-daysSinceMonday);
        }

        protected override void OnBarUpdate()
        {
if (CurrentBars[0] < 1)
                return;

            if (ShowProgressPrints && State == State.Historical) _swBody.Start();

            ApplyHideBars();

            // Feedback + diagnostic chargement (ShowProgressPrints) : l'UI restant
            // reactive, ces Prints streament en direct dans la fenetre Output.
            bool timeHist = ShowProgressPrints && State == State.Historical;
            if (timeHist && !_progStarted)
            {
                _progStarted = true;
                _progCount = Count;
                _progNext = Math.Max(1, _progCount / 10);
                _swTotal.Reset(); _swTotal.Start();
                _swBody.Reset(); _swAccum.Reset(); _swVA.Reset();
                try { Print(string.Format("[OPT] chargement debut, barres={0} {1:HH:mm:ss}", _progCount, DateTime.Now)); } catch {}
                try { Draw.Text(this, "MGIOPT_LOADING", false, "MGI OPT chargement 0%...", 0, Closes[0][0], 0, Brushes.DarkOrange, new Gui.Tools.SimpleFont("Arial", 16f), TextAlignment.Center, null, null, 0); } catch {}
            }
            else if (timeHist && _progStarted && _progCount > 0 && CurrentBar >= _progNext)
            {
                try { Print(string.Format("[OPT] {0}% ({1}/{2}) t={3:F0}s", (CurrentBar * 100) / _progCount, CurrentBar, _progCount, _swTotal.Elapsed.TotalSeconds)); } catch {}
                try { Draw.Text(this, "MGIOPT_LOADING", false, string.Format("MGI OPT chargement {0}%...", (CurrentBar * 100) / _progCount), 0, Closes[0][0], 0, Brushes.DarkOrange, new Gui.Tools.SimpleFont("Arial", 16f), TextAlignment.Center, null, null, 0); } catch {}
                _progNext += Math.Max(1, _progCount / 10);
            }

            // Sur NT8 Time[0] est la clôture : on raisonne en open pour que
            // overnight tracé à l'open (close précédente) et OR calculé à la
            // clôture ne soient pas décalés d'une bougie.
            DateTime barOpenTime = GetBarOpenTime();
            TimeSpan barOpenTOD = barOpenTime.TimeOfDay;

            // ------- JOUR -------
            // Clé du jour de trading : on considère que le "jour" commence au début de
            // la session overnight saisie (ex: 18:00). Un barre à 20:00 appartient au
            // jour calendaire du lendemain. Fonctionne sur tous les gabarits
            // de session (même sans coupure de session quotidienne).
            DateTime dayKey = barOpenTOD >= effOvernightStart
                ? barOpenTime.Date.AddDays(1)
                : barOpenTime.Date;

            bool newTradingDay = dayKey != currentDayKey || currentDayStartBar < 0;

            if (newTradingDay)
            {
                currentDayKey = dayKey;

                // Finalise le Volume Profile de la journée qui vient de se
                // terminer : elle devient le PRIOR DAY VAH / VAL / VPOC.
                // Le profil est TOUJOURS remis à zéro dès qu'un affichage VA est
                // actif (sinon le profil "en cours" cumulerait les jours passés).
                if (ShowDayPriorVA || ShowDayCurrentVA)
                {
                    ComputePriorProfile(currentDayProfile, ref priorDayVAH, ref priorDayVAL, ref priorDayVPOC);
                    if (ShowDayPriorVA)
                        hasPriorDayProfile = currentDayProfile.Count > 0;
                    currentDayProfile = new Dictionary<long, double>();
                }

                // Efface les barres de la journée précédente pour ne garder
                // que la journée en cours (une seule ligne par niveau).
                if (currentDayStartBar >= 0)
                {
                    for (int idx = currentDayStartBar; idx < CurrentBar; idx++)
                    {
                        PriorDayHigh[idx] = double.NaN;
                        PriorDayLow[idx] = double.NaN;
                        PriorDayOpen[idx] = double.NaN;
                        PriorDayClose[idx] = double.NaN;
                        CurrentDayHigh[idx] = double.NaN;
                        CurrentDayLow[idx] = double.NaN;
                        CurrentDayOpen[idx] = double.NaN;
                        OvernightHigh[idx] = double.NaN;
                        OvernightLow[idx] = double.NaN;
                        RTHOpen[idx] = double.NaN;
                        OpeningRangeHigh[idx] = double.NaN;
                        OpeningRangeLow[idx] = double.NaN;
                        OpeningRangeMid[idx] = double.NaN;
                        InitialBalanceHigh[idx] = double.NaN;
                        InitialBalanceLow[idx] = double.NaN;
                        InitialBalanceMid[idx] = double.NaN;
                        DayVWAP[idx] = double.NaN;
                    }
                }
                currentDayStartBar = CurrentBar;
                dayVwapSumPV = 0;
                dayVwapSumV = 0;
                dayVWAP = 0;

                if (hasPriorDay || currentDayOpen > 0)
                {
                    priorDayOpen = currentDayOpen;
                    priorDayHigh = currentDayHigh;
                    priorDayLow = currentDayLow;
                    priorDayClose = Closes[0][1];
                    hasPriorDay = true;
                }

                currentDayOpen = Opens[0][0];
                currentDayHigh = Highs[0][0];
                currentDayLow = Lows[0][0];
            }
            else
            {
                currentDayHigh = Math.Max(currentDayHigh, Highs[0][0]);
                currentDayLow = Math.Min(currentDayLow, Lows[0][0]);
            }

            // ------- SEMAINE -------
            DateTime barWeekKey = GetWeekKey(barOpenTime);
            if (barWeekKey != currentWeekKey)
            {
                // Finalise le Volume Profile de la semaine terminée : elle
                // devient le PRIOR WEEK VAH / VAL / VPOC.
                if (ShowWeekPriorVA || ShowWeekCurrentVA)
                {
                    ComputePriorProfile(currentWeekProfile, ref priorWeekVAH, ref priorWeekVAL, ref priorWeekVPOC);
                    if (ShowWeekPriorVA)
                        hasPriorWeekProfile = currentWeekProfile.Count > 0;
                    currentWeekProfile = new Dictionary<long, double>();
                }

                if (currentWeekStartBar >= 0)
                {
                    for (int idx = currentWeekStartBar; idx < CurrentBar; idx++)
                    {
                        PriorWeekHigh[idx] = double.NaN;
                        PriorWeekLow[idx] = double.NaN;
                        PriorWeekOpen[idx] = double.NaN;
                        PriorWeekClose[idx] = double.NaN;
                        CurrentWeekHigh[idx] = double.NaN;
                        CurrentWeekLow[idx] = double.NaN;
                        CurrentWeekOpen[idx] = double.NaN;
                        WeekVWAP[idx] = double.NaN;
                    }
                }
                currentWeekStartBar = CurrentBar;
                weekVwapSumPV = 0;
                weekVwapSumV = 0;
                weekVWAP = 0;

                if (hasCurrentWeek)
                {
                    priorWeekOpen = currentWeekOpen;
                    priorWeekHigh = currentWeekHigh;
                    priorWeekLow = currentWeekLow;
                    priorWeekClose = Closes[0][1];
                    hasPriorWeek = true;
                }

                currentWeekKey = barWeekKey;
                currentWeekOpen = Opens[0][0];
                currentWeekHigh = Highs[0][0];
                currentWeekLow = Lows[0][0];
                hasCurrentWeek = true;
            }
            else
            {
                currentWeekHigh = Math.Max(currentWeekHigh, Highs[0][0]);
                currentWeekLow = Math.Min(currentWeekLow, Lows[0][0]);
            }

            // ------- MOIS -------
            DateTime barMonthKey = new DateTime(barOpenTime.Year, barOpenTime.Month, 1);
            if (barMonthKey != currentMonthKey)
            {
                // Finalise le Volume Profile du mois terminé : il devient
                // le PRIOR MONTH VAH / VAL / VPOC.
                if (ShowMonthPriorVA || ShowMonthCurrentVA)
                {
                    ComputePriorProfile(currentMonthProfile, ref priorMonthVAH, ref priorMonthVAL, ref priorMonthVPOC);
                    if (ShowMonthPriorVA)
                        hasPriorMonthProfile = currentMonthProfile.Count > 0;
                    currentMonthProfile = new Dictionary<long, double>();
                }

                if (currentMonthStartBar >= 0)
                {
                    for (int idx = currentMonthStartBar; idx < CurrentBar; idx++)
                    {
                        PriorMonthHigh[idx] = double.NaN;
                        PriorMonthLow[idx] = double.NaN;
                        PriorMonthOpen[idx] = double.NaN;
                        PriorMonthClose[idx] = double.NaN;
                        CurrentMonthHigh[idx] = double.NaN;
                        CurrentMonthLow[idx] = double.NaN;
                        CurrentMonthOpen[idx] = double.NaN;
                        MonthVWAP[idx] = double.NaN;
                    }
                }
                currentMonthStartBar = CurrentBar;
                monthVwapSumPV = 0;
                monthVwapSumV = 0;
                monthVWAP = 0;

                if (hasCurrentMonth)
                {
                    priorMonthOpen = currentMonthOpen;
                    priorMonthHigh = currentMonthHigh;
                    priorMonthLow = currentMonthLow;
                    priorMonthClose = Closes[0][1];
                    hasPriorMonth = true;
                }

                currentMonthKey = barMonthKey;
                currentMonthOpen = Opens[0][0];
                currentMonthHigh = Highs[0][0];
                currentMonthLow = Lows[0][0];
                hasCurrentMonth = true;
            }
            else
            {
                currentMonthHigh = Math.Max(currentMonthHigh, Highs[0][0]);
                currentMonthLow = Math.Min(currentMonthLow, Lows[0][0]);
            }

            // ------- OVERNIGHT (avant ouverture RTH) -------
            // La session overnight commence la veille au soir (ex: 18:00) et se
            // termine le lendemain matin à l'ouverture RTH (ex: 09:30).
            // Elle franchit donc minuit : on accumule si l'heure est >= start
            // (soir) OU < RTH open (matin), et on fige dès qu'on entre en RTH.
            if (newTradingDay)
            {
                overnightHigh = Highs[0][0];
                overnightLow = Lows[0][0];
                hasOvernight = true;
                overnightFinalized = false;

                // Démarre l'accumulation du Volume Profile overnight.
                if (ShowOvernightProfile)
                {
                    hasOvernightProfile = false;
                    currentOvernightProfile = new Dictionary<long, double>();
                }
            }
            else if (!overnightFinalized)
            {
                bool isOvernightBar = barOpenTOD >= effOvernightStart || barOpenTOD < effRTHOpen;

                if (isOvernightBar)
                {
                    overnightHigh = Math.Max(overnightHigh, Highs[0][0]);
                    overnightLow = Math.Min(overnightLow, Lows[0][0]);
                }
                else
                {
                    // On est dans la fenêtre RTH (entre RTH open et start) :
                    // le niveau overnight est figé pour la journée.
                    overnightFinalized = true;
                }
            }

            // ------- RTH SESSION -------
            if (newTradingDay)
            {
                rthStarted = false;
                orActive = false;
                orFinalized = false;
                ibActive = false;
                ibFinalized = false;
                rthStartBar = -1;
                orEndBar = -1;
                ibEndBar = -1;
            }

            if (!rthStarted && barOpenTOD >= effRTHOpen && barOpenTOD < effOvernightStart)
            {
                rthStarted = true;
                rthStartBar = CurrentBar;
                rthOpen = Opens[0][0];

                // Le Volume Profile overnight est figé dès l'ouverture RTH.
                if (ShowOvernightProfile)
                {
                    ComputePriorProfile(currentOvernightProfile, ref overnightVAH, ref overnightVAL, ref overnightVPOC);
                    hasOvernightProfile = true;
                }
            }

            // Opening Range : les N premières minutes après l'open RTH
            // (raisonné en open de bougie, pas close, pour éviter le décalage NT8)
            if (rthStarted && !orFinalized)
            {
                TimeSpan orEnd = effRTHOpen.Add(TimeSpan.FromMinutes(OpeningRangeMinutes));
                if (barOpenTOD >= effRTHOpen && barOpenTOD < orEnd)
                {
                    if (!orActive)
                    {
                        orActive = true;
                        orHigh = Highs[0][0];
                        orLow = Lows[0][0];
                    }
                    else
                    {
                        orHigh = Math.Max(orHigh, Highs[0][0]);
                        orLow = Math.Min(orLow, Lows[0][0]);
                    }
                }
                else if (barOpenTOD >= orEnd && orActive)
                {
                    orFinalized = true;
                    orEndBar = CurrentBar;
                    orActive = false;
                }
            }

            // Initial Balance : fenêtre horaire saisie manuellement (défaut 14:30 - 15:30).
            if (rthStarted && !ibFinalized)
            {
                if (barOpenTOD >= IBStartTime && barOpenTOD < IBEndTime)
                {
                    if (!ibActive)
                    {
                        ibActive = true;
                        ibHigh = Highs[0][0];
                        ibLow = Lows[0][0];
                    }
                    else
                    {
                        ibHigh = Math.Max(ibHigh, Highs[0][0]);
                        ibLow = Math.Min(ibLow, Lows[0][0]);
                    }
                }
                else if (barOpenTOD >= IBEndTime && ibActive)
                {
                    ibFinalized = true;
                    ibEndBar = CurrentBar;
                    ibActive = false;
                }
            }

            // ------- VOLUME PROFILE (accumulation) -------
            // Distribue le volume de la barre sur les niveaux de prix compris
            // entre Low et High (pas = TickSize). Les profils accumulés servent
            // à calculer VAH / VAL / VPOC du lendemain / la semaine / le mois.
            // P0-opt : boucle UNIQUE sur les niveaux pour les 4 profils (meme range
            // de prix par barre) au lieu de 4 boucles + 4x Floor. ~3x plus rapide sur
            // le chemin chaud historique (780k barres a 60j x 45 series). Meme math
            // (division par profileStep conservee a l'identique), memes filtres RTH.
            if (timeHist) _swAccum.Start();
            double barVolume = Volumes[0][0];
            bool rthBar = true;
            if (ProfileUseRTHOnly)
            {
                if (_accCacheBar != CurrentBar)
                {
                    _accCacheTOD = GetBarOpenTimeOfDay();
                    _accCacheBar = CurrentBar;
                }
                rthBar = (_accCacheTOD >= effRTHOpen && _accCacheTOD < effOvernightStart);
            }
            bool accDay = (ShowDayPriorVA || ShowDayCurrentVA) && rthBar;
            bool accWeek = (ShowWeekPriorVA || ShowWeekCurrentVA) && rthBar;
            bool accMonth = (ShowMonthPriorVA || ShowMonthCurrentVA) && rthBar;
            bool accOvernight = ShowOvernightProfile && !rthStarted
                && (barOpenTOD >= effOvernightStart || barOpenTOD < effRTHOpen);
            if (accDay || accWeek || accMonth || accOvernight)
                AccumulateProfiles(barVolume, accDay, accWeek, accMonth, accOvernight);
            if (timeHist) _swAccum.Stop();

            // ------- VAH / VAL / VPOC LIVE (fixe + developing) -------
            // Recalcule à chaque barre la zone de valeur de la période en cours
            // à partir du profil accumulé (permettra les lignes "fixe" et les
            // traces "developing" qui montrent l'évolution depuis la session).
            // P0-opt : en historique (sauf derniere barre) on saute ce recalcul :
            // chaque appel balaie le profil ENTIER (semaine/mois = milliers de
            // niveaux) pour un resultat jamais affiche (Draw skippé, plots VA
            // inexistants — seules les 3 traces VWAP ecrivent en [0]). C'etait le
            // poste qui figeait le thread UI (meme le "Calculating..." ne peignait plus).
            // L'accumulation ci-dessus, elle, reste active sur chaque barre.
            if (timeHist) _swVA.Start();
            bool needLiveVA = !(State == State.Historical && CurrentBar < Count - 1);
            if (needLiveVA && ShowDayCurrentVA)
                ComputePriorProfile(currentDayProfile, ref currentDayVAH, ref currentDayVAL, ref currentDayVPOC);
            if (needLiveVA && ShowWeekCurrentVA)
                ComputePriorProfile(currentWeekProfile, ref currentWeekVAH, ref currentWeekVAL, ref currentWeekVPOC);
            if (needLiveVA && ShowMonthCurrentVA)
                ComputePriorProfile(currentMonthProfile, ref currentMonthVAH, ref currentMonthVAL, ref currentMonthVPOC);
            if (timeHist) _swVA.Stop();

            // ------- VWAP CUMULÉS (jour / semaine / mois) -------
            double typicalPrice = (Highs[0][0] + Lows[0][0] + Closes[0][0]) / 3.0;
            dayVwapSumPV += typicalPrice * barVolume;
            dayVwapSumV += barVolume;
            if (dayVwapSumV > 0)
                dayVWAP = dayVwapSumPV / dayVwapSumV;

            weekVwapSumPV += typicalPrice * barVolume;
            weekVwapSumV += barVolume;
            if (weekVwapSumV > 0)
                weekVWAP = weekVwapSumPV / weekVwapSumV;

            monthVwapSumPV += typicalPrice * barVolume;
            monthVwapSumV += barVolume;
            if (monthVwapSumV > 0)
                monthVWAP = monthVwapSumPV / monthVwapSumV;

            // ------- VWAP (traces cumulées) -------
            if (ShowDayVWAP)
                DayVWAP[0] = dayVWAP;
            if (ShowWeekVWAP)
                WeekVWAP[0] = weekVWAP;
            if (ShowMonthVWAP)
                MonthVWAP[0] = monthVWAP;

            // ------- AFFICHAGE (Draw) -------
            // P0-opt : en historique on ne dessine rien (calculs + plots ci-dessus
            // restent actifs). La 1re barre temps reel redessine tout avec les
            // priors finaux : ~99% des Draw du chargement elimines, visuel identique.
            // (5min x 45 series au chargement = 600k+ OnBarUpdate : seule la derniere
            // barre historique compte, le tag reutilise ecrase de toute facon. On DOIT
            // dessiner sur cette derniere barre, sinon le chart reste vide jusqu'a la
            // prochaine cloture temps reel — et jamais si le marche est ferme.)
            if (State == State.Historical && CurrentBar < Count - 1)
            {
                if (ShowProgressPrints) _swBody.Stop();
                return;
            }

            // Fin du chargement : 1re execution du bloc affichage (derniere barre
            // historique ou temps reel). Bilan une seule fois + retrait du marqueur.
            if (ShowProgressPrints && _progStarted)
            {
                _swTotal.Stop();
                _progStarted = false;
                try { Print(string.Format("[OPT] chargement termine en {0:F1}s wall (code={1:F1}s, accum={2:F1}s, VA={3:F1}s, barres={4})", _swTotal.Elapsed.TotalSeconds, _swBody.Elapsed.TotalSeconds, _swAccum.Elapsed.TotalSeconds, _swVA.Elapsed.TotalSeconds, _progCount)); } catch {}
                _progDoneAt = DateTime.Now;
                try { RemoveDrawObject("MGIOPT_LOADING"); } catch {}
            }
            // Chaque niveau est dessiné comme UNE ligne unique, depuis la barre
            // de début de la période courante jusqu'à la barre courante.
            // Comme le tag est réutilisé, NT8 remplace l'ancienne ligne :
            // les niveaux des périodes passées disparaissent automatiquement.
            if (ShowDay && hasPriorDay && currentDayStartBar >= 0)
            {
                int bars = CurrentBar - currentDayStartBar;
                DrawLevel("MGIDayHigh", "PDH", bars, priorDayHigh, DailyColor, DashStyleHelper.Solid, DailyLineWidth);
                DrawLevel("MGIDayLow", "PDL", bars, priorDayLow, DailyColor, DashStyleHelper.Solid, DailyLineWidth);
                DrawLevel("MGIDayOpen", "PDO", bars, priorDayOpen, DailyColor, DashStyleHelper.Solid, DailyLineWidth);
                DrawLevel("MGIDayClose", "PDC", bars, priorDayClose, DailyColor, DashStyleHelper.Solid, DailyLineWidth);
            }
            else
            {
                RemoveLevel("MGIDayHigh");
                RemoveLevel("MGIDayLow");
                RemoveLevel("MGIDayOpen");
                RemoveLevel("MGIDayClose");
            }

            if (ShowWeek && hasPriorWeek && currentWeekStartBar >= 0)
            {
                int bars = CurrentBar - currentWeekStartBar;
                DrawLevel("MGIWeekHigh", "PWH", bars, priorWeekHigh, WeeklyColor, DashStyleHelper.Solid, WeeklyLineWidth);
                DrawLevel("MGIWeekLow", "PWL", bars, priorWeekLow, WeeklyColor, DashStyleHelper.Solid, WeeklyLineWidth);
                DrawLevel("MGIWeekOpen", "PWO", bars, priorWeekOpen, WeeklyColor, DashStyleHelper.Solid, WeeklyLineWidth);
                DrawLevel("MGIWeekClose", "PWC", bars, priorWeekClose, WeeklyColor, DashStyleHelper.Solid, WeeklyLineWidth);
            }
            else
            {
                RemoveLevel("MGIWeekHigh");
                RemoveLevel("MGIWeekLow");
                RemoveLevel("MGIWeekOpen");
                RemoveLevel("MGIWeekClose");
            }

            if (ShowMonth && hasPriorMonth && currentMonthStartBar >= 0)
            {
                int bars = CurrentBar - currentMonthStartBar;
                DrawLevel("MGIMonthHigh", "PMH", bars, priorMonthHigh, MonthlyColor, DashStyleHelper.Solid, MonthlyLineWidth);
                DrawLevel("MGIMonthLow", "PML", bars, priorMonthLow, MonthlyColor, DashStyleHelper.Solid, MonthlyLineWidth);
                DrawLevel("MGIMonthOpen", "PMO", bars, priorMonthOpen, MonthlyColor, DashStyleHelper.Solid, MonthlyLineWidth);
                DrawLevel("MGIMonthClose", "PMC", bars, priorMonthClose, MonthlyColor, DashStyleHelper.Solid, MonthlyLineWidth);
            }
            else
            {
                RemoveLevel("MGIMonthHigh");
                RemoveLevel("MGIMonthLow");
                RemoveLevel("MGIMonthOpen");
                RemoveLevel("MGIMonthClose");
            }

            // ------- PRIOR DAY VOLUME PROFILE (VAH / VAL / VPOC) -------
            if (ShowDayPriorVA && hasPriorDayProfile && currentDayStartBar >= 0)
            {
                int bars = CurrentBar - currentDayStartBar;
                DrawLevel("MGIPVAH", "PDVAH", bars, priorDayVAH, DailyColor, DashStyleHelper.Solid, DailyLineWidth);
                DrawLevel("MGIPVAL", "PDVAL", bars, priorDayVAL, DailyColor, DashStyleHelper.Solid, DailyLineWidth);
                DrawLevel("MGIPVPOC", "PDPOC", bars, priorDayVPOC, DailyColor, DashStyleHelper.Dot, DailyLineWidth);
            }
            else
            {
                RemoveLevel("MGIPVAH");
                RemoveLevel("MGIPVAL");
                RemoveLevel("MGIPVPOC");
            }

            // ------- PRIOR WEEK VOLUME PROFILE (VAH / VAL / VPOC) -------
            if (ShowWeekPriorVA && hasPriorWeekProfile && currentWeekStartBar >= 0)
            {
                int bars = CurrentBar - currentWeekStartBar;
                DrawLevel("MGIPWVAH", "PWVAH", bars, priorWeekVAH, WeeklyColor, DashStyleHelper.Solid, WeeklyLineWidth);
                DrawLevel("MGIPWVAL", "PWVAL", bars, priorWeekVAL, WeeklyColor, DashStyleHelper.Solid, WeeklyLineWidth);
                DrawLevel("MGIPWVPOC", "PWPOC", bars, priorWeekVPOC, WeeklyColor, DashStyleHelper.Dot, WeeklyLineWidth);
            }
            else
            {
                RemoveLevel("MGIPWVAH");
                RemoveLevel("MGIPWVAL");
                RemoveLevel("MGIPWVPOC");
            }

            // ------- PRIOR MONTH VOLUME PROFILE (VAH / VAL / VPOC) -------
            if (ShowMonthPriorVA && hasPriorMonthProfile && currentMonthStartBar >= 0)
            {
                int bars = CurrentBar - currentMonthStartBar;
                DrawLevel("MGIPMVAH", "PMVAH", bars, priorMonthVAH, MonthlyColor, DashStyleHelper.Solid, MonthlyLineWidth);
                DrawLevel("MGIPMVAL", "PMVAL", bars, priorMonthVAL, MonthlyColor, DashStyleHelper.Solid, MonthlyLineWidth);
                DrawLevel("MGIPMVPOC", "PMPOC", bars, priorMonthVPOC, MonthlyColor, DashStyleHelper.Dot, MonthlyLineWidth);
            }
            else
            {
                RemoveLevel("MGIPMVAH");
                RemoveLevel("MGIPMVAL");
                RemoveLevel("MGIPMVPOC");
            }

            // ------- OVERNIGHT VOLUME PROFILE (VAH / VAL / VPOC) -------
            if (ShowOvernightProfile && hasOvernightProfile && rthStarted && rthStartBar >= 0)
            {
                int bars = CurrentBar - rthStartBar;
                DrawLevel("MGIPOnVAH", "ONVAH", bars, overnightVAH, OvernightColor, DashStyleHelper.Solid, OvernightLineWidth);
                DrawLevel("MGIPOnVAL", "ONVAL", bars, overnightVAL, OvernightColor, DashStyleHelper.Solid, OvernightLineWidth);
                DrawLevel("MGIPOnVPOC", "ONPOC", bars, overnightVPOC, OvernightColor, DashStyleHelper.Dot, OvernightLineWidth);
            }
            else
            {
                RemoveLevel("MGIPOnVAH");
                RemoveLevel("MGIPOnVAL");
                RemoveLevel("MGIPOnVPOC");
            }

            // ------- CURRENT DAY / WEEK / MONTH (lignes en cours) -------
            if (ShowCurrentDay && currentDayStartBar >= 0)
            {
                int bars = CurrentBar - currentDayStartBar;
                DrawLevel("MGICurDayHigh", "DH", bars, currentDayHigh, DailyColor, DashStyleHelper.Dot, DailyLineWidth);
                DrawLevel("MGICurDayLow", "DL", bars, currentDayLow, DailyColor, DashStyleHelper.Dot, DailyLineWidth);
                DrawLevel("MGICurDayOpen", "DO", bars, currentDayOpen, DailyColor, DashStyleHelper.Dot, DailyLineWidth);
            }
            else
            {
                RemoveLevel("MGICurDayHigh");
                RemoveLevel("MGICurDayLow");
                RemoveLevel("MGICurDayOpen");
            }

            if (ShowCurrentWeek && currentWeekStartBar >= 0)
            {
                int bars = CurrentBar - currentWeekStartBar;
                DrawLevel("MGICurWeekHigh", "WH", bars, currentWeekHigh, WeeklyColor, DashStyleHelper.Dot, WeeklyLineWidth);
                DrawLevel("MGICurWeekLow", "WL", bars, currentWeekLow, WeeklyColor, DashStyleHelper.Dot, WeeklyLineWidth);
                DrawLevel("MGICurWeekOpen", "WO", bars, currentWeekOpen, WeeklyColor, DashStyleHelper.Dot, WeeklyLineWidth);
            }
            else
            {
                RemoveLevel("MGICurWeekHigh");
                RemoveLevel("MGICurWeekLow");
                RemoveLevel("MGICurWeekOpen");
            }

            if (ShowCurrentMonth && currentMonthStartBar >= 0)
            {
                int bars = CurrentBar - currentMonthStartBar;
                DrawLevel("MGICurMonthHigh", "MH", bars, currentMonthHigh, MonthlyColor, DashStyleHelper.Dot, MonthlyLineWidth);
                DrawLevel("MGICurMonthLow", "ML", bars, currentMonthLow, MonthlyColor, DashStyleHelper.Dot, MonthlyLineWidth);
                DrawLevel("MGICurMonthOpen", "MO", bars, currentMonthOpen, MonthlyColor, DashStyleHelper.Dot, MonthlyLineWidth);
            }
            else
            {
                RemoveLevel("MGICurMonthHigh");
                RemoveLevel("MGICurMonthLow");
                RemoveLevel("MGICurMonthOpen");
            }

            // ------- CURRENT VOLUME PROFILE (lignes "fixe" live) -------
            // Une ligne horizontale à la valeur actuelle de la VA, tracée sur
            // toute la session. Les traces "developing" (plots) montrent, elles,
            // l'évolution de la VA depuis le début de la session.
            if (ShowDayCurrentVA && currentDayProfile != null && currentDayProfile.Count > 0 && currentDayStartBar >= 0)
            {
                int bars = CurrentBar - currentDayStartBar;
                DrawLevel("MGIDVAH", "DVAH", bars, currentDayVAH, DailyColor, DashStyleHelper.Solid, DailyLineWidth);
                DrawLevel("MGIDVAL", "DVAL", bars, currentDayVAL, DailyColor, DashStyleHelper.Solid, DailyLineWidth);
                DrawLevel("MGIDVPOC", "DPOC", bars, currentDayVPOC, DailyColor, DashStyleHelper.Dot, DailyLineWidth);
            }
            else
            {
                RemoveLevel("MGIDVAH");
                RemoveLevel("MGIDVAL");
                RemoveLevel("MGIDVPOC");
            }

            if (ShowWeekCurrentVA && currentWeekProfile != null && currentWeekProfile.Count > 0 && currentWeekStartBar >= 0)
            {
                int bars = CurrentBar - currentWeekStartBar;
                DrawLevel("MGIWVAH", "WVAH", bars, currentWeekVAH, WeeklyColor, DashStyleHelper.Solid, WeeklyLineWidth);
                DrawLevel("MGIWVAL", "WVAL", bars, currentWeekVAL, WeeklyColor, DashStyleHelper.Solid, WeeklyLineWidth);
                DrawLevel("MGIWVPOC", "WPOC", bars, currentWeekVPOC, WeeklyColor, DashStyleHelper.Dot, WeeklyLineWidth);
            }
            else
            {
                RemoveLevel("MGIWVAH");
                RemoveLevel("MGIWVAL");
                RemoveLevel("MGIWVPOC");
            }

            if (ShowMonthCurrentVA && currentMonthProfile != null && currentMonthProfile.Count > 0 && currentMonthStartBar >= 0)
            {
                int bars = CurrentBar - currentMonthStartBar;
                DrawLevel("MGIMVAH", "MVAH", bars, currentMonthVAH, MonthlyColor, DashStyleHelper.Solid, MonthlyLineWidth);
                DrawLevel("MGIMVAL", "MVAL", bars, currentMonthVAL, MonthlyColor, DashStyleHelper.Solid, MonthlyLineWidth);
                DrawLevel("MGIMVPOC", "MPOC", bars, currentMonthVPOC, MonthlyColor, DashStyleHelper.Dot, MonthlyLineWidth);
            }
            else
            {
                RemoveLevel("MGIMVAH");
                RemoveLevel("MGIMVAL");
                RemoveLevel("MGIMVPOC");
            }

            // ------- VWAP (développement uniquement) -------
            // Les VWAP jour / semaine / mois sont affichés uniquement sous
            // forme de traces "developing" (plots DayVWAP / WeekVWAP / MonthVWAP).
            RemoveLevel("MGIDVWAP");
            RemoveLevel("MGIWVWAP");
            RemoveLevel("MGIMVWAP");

            if (ShowOvernight && hasOvernight && overnightFinalized && rthStartBar >= 0)
            {
                // N'affiche les lignes que de l'open RTH jusqu'à la clôture
                // de la journée (le niveau est figé une fois l'RTH commencé).
                int bars = CurrentBar - rthStartBar;
                DrawLevel("MGIOnHigh", "ONH", bars, overnightHigh, OvernightColor, DashStyleHelper.Dot, OvernightLineWidth);
                DrawLevel("MGIOnLow", "ONL", bars, overnightLow, OvernightColor, DashStyleHelper.Dot, OvernightLineWidth);
            }
            else
            {
                RemoveLevel("MGIOnHigh");
                RemoveLevel("MGIOnLow");
            }

            if (ShowRTH && rthStarted && rthStartBar >= 0)
            {
                int bars = CurrentBar - rthStartBar;
                DrawLevel("MGIRTHOpen", "RTH", bars, rthOpen, RTHColor, DashStyleHelper.Solid, RTHLineWidth);
            }
            else
            {
                RemoveLevel("MGIRTHOpen");
            }

            if (ShowOpeningRange && orFinalized && orEndBar >= 0)
            {
                // N'affiche les lignes qu'après la fin de la fenêtre OR
                // (N minutes après l'open RTH, 5 par défaut) : la ligne
                // démarre à la barre de fin de fenêtre, pas à l'open.
                int bars = CurrentBar - orEndBar;
                DrawLevel("MGIORHigh", "ORH", bars, orHigh, ORColor, DashStyleHelper.Solid, ORLineWidth);
                DrawLevel("MGIORLow", "ORL", bars, orLow, ORColor, DashStyleHelper.Solid, ORLineWidth);
                DrawLevel("MGIORMid", "ORM", bars, (orHigh + orLow) / 2.0, ORColor, DashStyleHelper.Solid, ORLineWidth);
            }
            else
            {
                RemoveLevel("MGIORHigh");
                RemoveLevel("MGIORLow");
                RemoveLevel("MGIORMid");
            }

            if (ShowInitialBalance && ibFinalized && ibEndBar >= 0)
            {
                // N'affiche les lignes qu'après la fin de la fenêtre IB
                // (15:30 par défaut) : la ligne démarre à la barre de fin
                // de fenêtre, pas à l'open.
                int bars = CurrentBar - ibEndBar;
                DrawLevel("MGIIBHigh", "IBH", bars, ibHigh, IBColor, DashStyleHelper.Solid, IBLineWidth);
                DrawLevel("MGIIBLow", "IBL", bars, ibLow, IBColor, DashStyleHelper.Solid, IBLineWidth);
                DrawLevel("MGIIBMid", "IBM", bars, (ibHigh + ibLow) / 2.0, IBColor, DashStyleHelper.Solid, IBLineWidth);
            }
            else
            {
                RemoveLevel("MGIIBHigh");
                RemoveLevel("MGIIBLow");
                RemoveLevel("MGIIBMid");
            }
            if (ShowProgressPrints && State == State.Historical) _swBody.Stop();
        }

        private void RemoveLevel(string tag)
        {
            RemoveDrawObject(tag);
            RemoveDrawObject(tag + "_Lbl");
        }

        // P0-opt : labelTime calcule 1x par barre (GetXByTime couteux), reutilise
        // par les ~50 DrawLevel de la barre. En historique on n'arrive jamais ici
        // (return plus haut), donc pas de ChartControl null au chargement.
        private DateTime _cachedLabelTime = DateTime.MinValue;
        private int _cachedLabelBar = -1;
        private void DrawLevel(string tag, string label, int startBars, double level, Brush brush, DashStyleHelper dashStyle, int width)
        {
            Line line = Draw.Line(this, tag, startBars, level, 0, level, brush);
            line.Stroke = new Stroke(brush, dashStyle, width);
            if (!string.IsNullOrEmpty(label))
            {
                // Ancre le label légèrement à droite de la fin de la ligne
                // (LabelOffsetX pixels), à une hauteur décalée de LabelOffsetY
                // pixels, avec une taille de police réglable.
                DateTime labelTime;
                if (_cachedLabelBar == CurrentBar && _cachedLabelTime != DateTime.MinValue)
                    labelTime = _cachedLabelTime;
                else
                {
                    try { labelTime = ChartControl.GetTimeByX((int)(ChartControl.GetXByTime(Time[0]) + LabelOffsetX)); }
                    catch { labelTime = Time[0]; }
                    _cachedLabelTime = labelTime;
                    _cachedLabelBar = CurrentBar;
                }
                Draw.Text(this, tag + "_Lbl", false, label, labelTime, level, (int)LabelOffsetY, brush,
                    new Gui.Tools.SimpleFont("Arial", (float)LabelFontSize), TextAlignment.Left, null, null, 0);
            }
        }

        // Sur NT8 l'heure affichée sous une bougie et la valeur Time[0] correspondent à la
        // clôture. Pour que les fenêtres Overnight (ex: 18:00), OR (14:30-14:35) et IB
        // (14:30-15:30) soient alignées sur l'ouverture réelle de la bougie, on recule
        // Time[0] de la durée du BarsPeriod pour les barres à temps fixe. Pour les
        // barres Tick/Volume/Range on approxime l'open par la clôture précédente.
        private DateTime GetBarOpenTime()
        {
            if (CurrentBar <= 0)
                return Time[0];
            try
            {
                switch (BarsPeriod.BarsPeriodType)
                {
                    case BarsPeriodType.Minute:
                        return Time[0].AddMinutes(-BarsPeriod.Value);
                    case BarsPeriodType.Second:
                        return Time[0].AddSeconds(-BarsPeriod.Value);
                    case BarsPeriodType.Day:
                        return Time[0].AddDays(-BarsPeriod.Value);
                    case BarsPeriodType.Week:
                        return Time[0].AddDays(-7 * BarsPeriod.Value);
                    default:
                        // Tick / Volume / Range : open = close précédente
                        return Time[1];
                }
            }
            catch
            {
                return CurrentBar > 0 ? Time[1] : Time[0];
            }
        }

        private TimeSpan GetBarOpenTimeOfDay()
        {
            return GetBarOpenTime().TimeOfDay;
        }

        private void ApplyHideBars()
        {
            // Masque les bougies en rendant leur pinceau transparent.
            // BarBrushes gère les barres OHLC, CandleOutlineBrushes les bougies.
            if (HideBars)
            {
                BarBrushes[0] = Brushes.Transparent;
                CandleOutlineBrushes[0] = Brushes.Transparent;
            }
            else
            {
                BarBrushes[0] = null;
                CandleOutlineBrushes[0] = null;
            }
        }

        // Répartit le volume d'une barre sur les niveaux de prix (pas = TickSize)
        // entre Low et High de la barre, pour TOUS les profils actifs en une seule
        // boucle (meme range par barre). Remplace 4 appels a l'ancienne version.
        private void AccumulateProfiles(double barVolume, bool doDay, bool doWeek, bool doMonth, bool doOvernight)
        {
            if (barVolume <= 0)
                return;
            if (!doDay && !doWeek && !doMonth && !doOvernight)
                return;
            if (currentDayProfile == null || currentWeekProfile == null || currentMonthProfile == null || currentOvernightProfile == null)
                return;

            double low = Lows[0][0];
            double high = Highs[0][0];
            if (high <= low)
                return;

            long lowLevel = (long)Math.Floor(low / profileStep);
            long highLevel = (long)Math.Floor(high / profileStep);
            long count = highLevel - lowLevel + 1;
            if (count <= 0)
                return;

            double perLevel = barVolume / count;
            for (long lvl = lowLevel; lvl <= highLevel; lvl++)
            {
                double existing;
                if (doDay)
                {
                    if (currentDayProfile.TryGetValue(lvl, out existing)) currentDayProfile[lvl] = existing + perLevel;
                    else currentDayProfile[lvl] = perLevel;
                }
                if (doWeek)
                {
                    if (currentWeekProfile.TryGetValue(lvl, out existing)) currentWeekProfile[lvl] = existing + perLevel;
                    else currentWeekProfile[lvl] = perLevel;
                }
                if (doMonth)
                {
                    if (currentMonthProfile.TryGetValue(lvl, out existing)) currentMonthProfile[lvl] = existing + perLevel;
                    else currentMonthProfile[lvl] = perLevel;
                }
                if (doOvernight)
                {
                    if (currentOvernightProfile.TryGetValue(lvl, out existing)) currentOvernightProfile[lvl] = existing + perLevel;
                    else currentOvernightProfile[lvl] = perLevel;
                }
            }
        }

        // Calcule VAH / VAL / VPOC à partir d'un profil accumulé.
        // VPOC = niveau ayant le plus de volume. Zone de valeur = X% du volume
        // total (VAPercent), construite en partant du VPOC et en s'étendant
        // vers les niveaux voisins ayant le plus de volume.
        // Optimisé : aucun LINQ (List.Sort + BinarySearch) et liste des niveaux
        // triés mise en cache par profil pour éviter de re-trier à chaque barre.
        private void ComputePriorProfile(Dictionary<long, double> profile, ref double vah, ref double val, ref double vpoc)
        {
            if (profile == null || profile.Count == 0)
                return;

            double totalVolume = 0;
            long pocLevel = 0;
            double maxVol = double.MinValue;
            foreach (KeyValuePair<long, double> kv in profile)
            {
                totalVolume += kv.Value;
                if (kv.Value > maxVol)
                {
                    maxVol = kv.Value;
                    pocLevel = kv.Key;
                }
            }
            if (totalVolume <= 0)
                return;
            vpoc = pocLevel * profileStep;

            // Liste triée des niveaux (cache par référence du profil). Si le
            // nombre de niveaux change, on re-trie ; sinon on réutilise.
            if (sortedLevelCache == null)
                sortedLevelCache = new Dictionary<Dictionary<long, double>, List<long>>();
            List<long> levels;
            if (!sortedLevelCache.TryGetValue(profile, out levels) || levels.Count != profile.Count)
            {
                levels = new List<long>(profile.Count);
                foreach (long key in profile.Keys)
                    levels.Add(key);
                levels.Sort();
                if (sortedLevelCache.Count >= 8)
                    sortedLevelCache.Clear();
                sortedLevelCache[profile] = levels;
            }

            int pocIndex = levels.BinarySearch(pocLevel);
            if (pocIndex < 0)
                pocIndex = ~pocIndex;
            if (pocIndex < 0 || pocIndex >= levels.Count)
                return;

            double included = profile[pocLevel];
            double target = totalVolume * (ValueAreaPercent / 100.0);
            int lo = pocIndex;
            int hi = pocIndex;

            while (included < target && (lo > 0 || hi < levels.Count - 1))
            {
                double volUp = hi < levels.Count - 1 ? profile[levels[hi + 1]] : double.MinValue;
                double volDown = lo > 0 ? profile[levels[lo - 1]] : double.MinValue;

                if (volUp >= volDown && hi < levels.Count - 1)
                {
                    hi++;
                    included += profile[levels[hi]];
                }
                else if (lo > 0)
                {
                    lo--;
                    included += profile[levels[lo]];
                }
                else
                {
                    hi++;
                    included += profile[levels[hi]];
                }
            }

            vah = levels[hi] * profileStep;
            val = levels[lo] * profileStep;
        }

        #region Properties

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PriorDayHigh
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PriorDayLow
        {
            get { return Values[1]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PriorDayOpen
        {
            get { return Values[2]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PriorDayClose
        {
            get { return Values[3]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PriorWeekHigh
        {
            get { return Values[4]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PriorWeekLow
        {
            get { return Values[5]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PriorWeekOpen
        {
            get { return Values[6]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PriorWeekClose
        {
            get { return Values[7]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PriorMonthHigh
        {
            get { return Values[8]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PriorMonthLow
        {
            get { return Values[9]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PriorMonthOpen
        {
            get { return Values[10]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PriorMonthClose
        {
            get { return Values[11]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> CurrentDayHigh
        {
            get { return Values[12]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> CurrentDayLow
        {
            get { return Values[13]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> CurrentDayOpen
        {
            get { return Values[14]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> CurrentWeekHigh
        {
            get { return Values[15]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> CurrentWeekLow
        {
            get { return Values[16]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> CurrentWeekOpen
        {
            get { return Values[17]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> CurrentMonthHigh
        {
            get { return Values[18]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> CurrentMonthLow
        {
            get { return Values[19]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> CurrentMonthOpen
        {
            get { return Values[20]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> OvernightHigh
        {
            get { return Values[21]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> OvernightLow
        {
            get { return Values[22]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> RTHOpen
        {
            get { return Values[23]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> OpeningRangeHigh
        {
            get { return Values[24]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> OpeningRangeLow
        {
            get { return Values[25]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> OpeningRangeMid
        {
            get { return Values[26]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> InitialBalanceHigh
        {
            get { return Values[27]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> InitialBalanceLow
        {
            get { return Values[28]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> InitialBalanceMid
        {
            get { return Values[29]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> DayVWAP
        {
            get { return Values[30]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> WeekVWAP
        {
            get { return Values[31]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> MonthVWAP
        {
            get { return Values[32]; }
        }

[NinjaScriptProperty]
        [Display(Name = "Niveaux Prior jour (PDH/PDL/PDO/PDC)", Description = "Affiche les niveaux High/Low/Open/Close de la séance quotidienne précédente.", Order = 1, GroupName = "Daily")]
        public bool ShowDay { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Volume Profile Prior jour (PDVAH/PDVAL/PDPOC)", Description = "Affiche VAH / VAL / VPOC de la journée précédente.", Order = 2, GroupName = "Daily")]
        public bool ShowDayPriorVA { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Niveaux jour en cours (DH/DL/DO)", Description = "Affiche les niveaux High/Low/Open de la journée en cours.", Order = 3, GroupName = "Daily")]
        public bool ShowCurrentDay { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Volume Profile jour en cours (DVAH/DVAL/DPOC)", Description = "Affiche VAH / VAL / VPOC live (fixe + developing) de la journée en cours.", Order = 4, GroupName = "Daily")]
        public bool ShowDayCurrentVA { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "VWAP jour (DVWAP)", Description = "Affiche le VWAP cumulé de la journée en cours.", Order = 5, GroupName = "Daily")]
        public bool ShowDayVWAP { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Niveaux Prior semaine (PWH/PWL/PWO/PWC)", Description = "Affiche les niveaux High/Low/Open/Close de la semaine précédente.", Order = 1, GroupName = "Weekly")]
        public bool ShowWeek { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Volume Profile Prior semaine (PWVAH/PWVAL/PWPOC)", Description = "Affiche VAH / VAL / VPOC de la semaine précédente.", Order = 2, GroupName = "Weekly")]
        public bool ShowWeekPriorVA { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Niveaux semaine en cours (WH/WL/WO)", Description = "Affiche les niveaux High/Low/Open de la semaine en cours.", Order = 3, GroupName = "Weekly")]
        public bool ShowCurrentWeek { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Volume Profile semaine en cours (WVAH/WVAL/WPOC)", Description = "Affiche VAH / VAL / VPOC live (fixe + developing) de la semaine en cours.", Order = 4, GroupName = "Weekly")]
        public bool ShowWeekCurrentVA { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "VWAP semaine (WVWAP)", Description = "Affiche le VWAP cumulé de la semaine en cours.", Order = 5, GroupName = "Weekly")]
        public bool ShowWeekVWAP { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Niveaux Prior mois (PMH/PML/PMO/PMC)", Description = "Affiche les niveaux High/Low/Open/Close du mois précédent.", Order = 1, GroupName = "Monthly")]
        public bool ShowMonth { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Volume Profile Prior mois (PMVAH/PMVAL/PMPOC)", Description = "Affiche VAH / VAL / VPOC du mois précédent.", Order = 2, GroupName = "Monthly")]
        public bool ShowMonthPriorVA { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Niveaux mois en cours (MH/ML/MO)", Description = "Affiche les niveaux High/Low/Open du mois en cours.", Order = 3, GroupName = "Monthly")]
        public bool ShowCurrentMonth { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Volume Profile mois en cours (MVAH/MVAL/MPOC)", Description = "Affiche VAH / VAL / VPOC live (fixe + developing) du mois en cours.", Order = 4, GroupName = "Monthly")]
        public bool ShowMonthCurrentVA { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "VWAP mois (MVWAP)", Description = "Affiche le VWAP cumulé du mois en cours.", Order = 5, GroupName = "Monthly")]
        public bool ShowMonthVWAP { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Niveaux Overnight (ONH/ONL)", Description = "Affiche les niveaux High/Low de la session overnight (avant ouverture RTH).", Order = 1, GroupName = "Overnight")]
        public bool ShowOvernight { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Volume Profile Overnight (ONVAH/ONVAL/ONPOC)", Description = "Affiche VAH / VAL / VPOC de la session overnight.", Order = 2, GroupName = "Overnight")]
        public bool ShowOvernightProfile { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Début overnight", Description = "Heure de début de la session overnight (veille au soir).", Order = 3, GroupName = "Overnight")]
        public TimeSpan OvernightStartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Heure ouverture RTH", Description = "Heure à laquelle la session overnight est considérée terminée (le niveau est figé).", Order = 4, GroupName = "Overnight")]
        public TimeSpan OvernightRTHOpenTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "RTH Open", Description = "Affiche l'Open de la session RTH.", Order = 1, GroupName = "Opening Range")]
        public bool ShowRTH { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Niveaux Opening Range (ORH/ORL/ORM)", Description = "Affiche ORH / ORL / ORMid.", Order = 2, GroupName = "Opening Range")]
        public bool ShowOpeningRange { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Durée Opening Range (min)", Description = "Nombre de minutes de l'Opening Range après l'open RTH.", Order = 3, GroupName = "Opening Range")]
        public int OpeningRangeMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Niveaux Initial Balance (IBH/IBL/IBM)", Description = "Affiche IBH / IBL / IBMid.", Order = 1, GroupName = "Initial Balance")]
        public bool ShowInitialBalance { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Début Initial Balance", Description = "Heure de début de la fenêtre Initial Balance.", Order = 2, GroupName = "Initial Balance")]
        public TimeSpan IBStartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Fin Initial Balance", Description = "Heure de fin de la fenêtre Initial Balance.", Order = 3, GroupName = "Initial Balance")]
        public TimeSpan IBEndTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "% Zone de Valeur", Description = "Pourcentage du volume total couvert par la zone de valeur (VAH-VAL).", Order = 1, GroupName = "Volume Profile")]
        public double ValueAreaPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Profil RTH uniquement", Description = "Ne calcule le profil que sur la session RTH (sinon 24h).", Order = 2, GroupName = "Volume Profile")]
        public bool ProfileUseRTHOnly { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Ticks par level", Description = "Nombre de ticks regroupés par level du Volume Profile. 1 = niveau au tick. Plus la valeur est élevée, plus le profil est condensé et le calcul rapide.", Order = 3, GroupName = "Volume Profile")]
        public int TicksPerLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Décalage horizontal labels", Description = "Distance en pixels entre la fin des lignes et le début des labels (vers la droite).", Order = 1, GroupName = "Labels")]
        public double LabelOffsetX { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Décalage vertical labels", Description = "Distance verticale en pixels des labels par rapport à la ligne.", Order = 2, GroupName = "Labels")]
        public double LabelOffsetY { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Taille du texte des labels", Description = "Taille de police (points) du texte des labels.", Order = 3, GroupName = "Labels")]
        public double LabelFontSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cacher les bougies", Description = "Masque les bougies du chart (rend transparent) pour ne garder que les niveaux de l'indicateur. Idéal pour une vue épurée.", Order = 11, GroupName = "Affichage")]
        public bool HideBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Prints de progression", Description = "Affiche la progression du chargement historique dans la fenetre Output (debut / ~10% / temps total + detail accum/VA). True recommande pour diagnostiquer, false pour silence total.", Order = 12, GroupName = "Affichage")]
        public bool ShowProgressPrints { get; set; }

        [XmlIgnore]
        [Display(Name = "Épaisseur lignes JOUR", Description = "Épaisseur (px) de toutes les lignes de la catégorie quotidienne (prior, current, volume profile, VWAP).", Order = 4, GroupName = "Labels")]
        public int DailyLineWidth { get; set; }

        [XmlIgnore]
        [Display(Name = "Épaisseur lignes SEMAINE", Description = "Épaisseur (px) de toutes les lignes de la catégorie hebdomadaire (prior, current, volume profile, VWAP).", Order = 5, GroupName = "Labels")]
        public int WeeklyLineWidth { get; set; }

        [XmlIgnore]
        [Display(Name = "Épaisseur lignes MOIS", Description = "Épaisseur (px) de toutes les lignes de la catégorie mensuelle (prior, current, volume profile, VWAP).", Order = 6, GroupName = "Labels")]
        public int MonthlyLineWidth { get; set; }

        [XmlIgnore]
        [Display(Name = "Épaisseur lignes OVERNIGHT", Description = "Épaisseur (px) de toutes les lignes de la catégorie overnight (high/low et volume profile).", Order = 7, GroupName = "Labels")]
        public int OvernightLineWidth { get; set; }

        [XmlIgnore]
        [Display(Name = "Épaisseur ligne RTH OPEN", Description = "Épaisseur (px) de la ligne d'ouverture RTH.", Order = 8, GroupName = "Labels")]
        public int RTHLineWidth { get; set; }

        [XmlIgnore]
        [Display(Name = "Épaisseur lignes OPENING RANGE", Description = "Épaisseur (px) de toutes les lignes de l'Opening Range.", Order = 9, GroupName = "Labels")]
        public int ORLineWidth { get; set; }

        [XmlIgnore]
        [Display(Name = "Épaisseur lignes INITIAL BALANCE", Description = "Épaisseur (px) de toutes les lignes de l'Initial Balance.", Order = 10, GroupName = "Labels")]
        public int IBLineWidth { get; set; }

        // Les propriétés *Serialize (Browsable(false)) permettent à NT8 de
        // sauvegarder les couleurs / épaisseurs dans les templates et les
        // paramètres de chart (les propriétés [XmlIgnore] ne sont pas
        // persistées seules).
        [Browsable(false)]
        public int DailyLineWidthSerialize
        {
            get => DailyLineWidth;
            set => DailyLineWidth = value;
        }

        [Browsable(false)]
        public int WeeklyLineWidthSerialize
        {
            get => WeeklyLineWidth;
            set => WeeklyLineWidth = value;
        }

        [Browsable(false)]
        public int MonthlyLineWidthSerialize
        {
            get => MonthlyLineWidth;
            set => MonthlyLineWidth = value;
        }

        [Browsable(false)]
        public int OvernightLineWidthSerialize
        {
            get => OvernightLineWidth;
            set => OvernightLineWidth = value;
        }

        [Browsable(false)]
        public int RTHLineWidthSerialize
        {
            get => RTHLineWidth;
            set => RTHLineWidth = value;
        }

        [Browsable(false)]
        public int ORLineWidthSerialize
        {
            get => ORLineWidth;
            set => ORLineWidth = value;
        }

        [Browsable(false)]
        public int IBLineWidthSerialize
        {
            get => IBLineWidth;
            set => IBLineWidth = value;
        }

[XmlIgnore]
        [Display(Name = "Couleur JOUR", Description = "Couleur de tous les niveaux quotidiens (prior, current, volume profile).", Order = 4, GroupName = "Couleurs")]
        public Brush DailyColor { get; set; }

        [XmlIgnore]
        [Display(Name = "Couleur SEMAINE", Description = "Couleur de tous les niveaux hebdomadaires (prior, current, volume profile).", Order = 5, GroupName = "Couleurs")]
        public Brush WeeklyColor { get; set; }

        [XmlIgnore]
        [Display(Name = "Couleur MOIS", Description = "Couleur de tous les niveaux mensuels (prior, current, volume profile).", Order = 6, GroupName = "Couleurs")]
        public Brush MonthlyColor { get; set; }

        [XmlIgnore]
        [Display(Name = "Couleur OVERNIGHT", Description = "Couleur de tous les niveaux overnight (high/low et volume profile).", Order = 7, GroupName = "Couleurs")]
        public Brush OvernightColor { get; set; }

        [XmlIgnore]
        [Display(Name = "Couleur RTH OPEN", Description = "Couleur de l'ouverture RTH.", Order = 8, GroupName = "Couleurs")]
        public Brush RTHColor { get; set; }

        [XmlIgnore]
        [Display(Name = "Couleur OPENING RANGE", Description = "Couleur des niveaux de l'Opening Range.", Order = 9, GroupName = "Couleurs")]
        public Brush ORColor { get; set; }

        [XmlIgnore]
        [Display(Name = "Couleur INITIAL BALANCE", Description = "Couleur des niveaux de l'Initial Balance.", Order = 10, GroupName = "Couleurs")]
        public Brush IBColor { get; set; }

        [Browsable(false)]
        public string DailyColorSerialize
        {
            get => Serialize.BrushToString(DailyColor);
            set => DailyColor = Serialize.StringToBrush(value);
        }

        [Browsable(false)]
        public string WeeklyColorSerialize
        {
            get => Serialize.BrushToString(WeeklyColor);
            set => WeeklyColor = Serialize.StringToBrush(value);
        }

        [Browsable(false)]
        public string MonthlyColorSerialize
        {
            get => Serialize.BrushToString(MonthlyColor);
            set => MonthlyColor = Serialize.StringToBrush(value);
        }

        [Browsable(false)]
        public string OvernightColorSerialize
        {
            get => Serialize.BrushToString(OvernightColor);
            set => OvernightColor = Serialize.StringToBrush(value);
        }

        [Browsable(false)]
        public string RTHColorSerialize
        {
            get => Serialize.BrushToString(RTHColor);
            set => RTHColor = Serialize.StringToBrush(value);
        }

        [Browsable(false)]
        public string ORColorSerialize
        {
            get => Serialize.BrushToString(ORColor);
            set => ORColor = Serialize.StringToBrush(value);
        }

        [Browsable(false)]
        public string IBColorSerialize
        {
            get => Serialize.BrushToString(IBColor);
            set => IBColor = Serialize.StringToBrush(value);
        }

#endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MGIPriorDayOHLC_Opt[] cacheMGIPriorDayOHLC_Opt;
		public MGIPriorDayOHLC_Opt MGIPriorDayOHLC_Opt(bool showDay, bool showDayPriorVA, bool showCurrentDay, bool showDayCurrentVA, bool showDayVWAP, bool showWeek, bool showWeekPriorVA, bool showCurrentWeek, bool showWeekCurrentVA, bool showWeekVWAP, bool showMonth, bool showMonthPriorVA, bool showCurrentMonth, bool showMonthCurrentVA, bool showMonthVWAP, bool showOvernight, bool showOvernightProfile, TimeSpan overnightStartTime, TimeSpan overnightRTHOpenTime, bool showRTH, bool showOpeningRange, int openingRangeMinutes, bool showInitialBalance, TimeSpan iBStartTime, TimeSpan iBEndTime, double valueAreaPercent, bool profileUseRTHOnly, int ticksPerLevel, double labelOffsetX, double labelOffsetY, double labelFontSize, bool hideBars, bool showProgressPrints)
		{
			return MGIPriorDayOHLC_Opt(Input, showDay, showDayPriorVA, showCurrentDay, showDayCurrentVA, showDayVWAP, showWeek, showWeekPriorVA, showCurrentWeek, showWeekCurrentVA, showWeekVWAP, showMonth, showMonthPriorVA, showCurrentMonth, showMonthCurrentVA, showMonthVWAP, showOvernight, showOvernightProfile, overnightStartTime, overnightRTHOpenTime, showRTH, showOpeningRange, openingRangeMinutes, showInitialBalance, iBStartTime, iBEndTime, valueAreaPercent, profileUseRTHOnly, ticksPerLevel, labelOffsetX, labelOffsetY, labelFontSize, hideBars, showProgressPrints);
		}

		public MGIPriorDayOHLC_Opt MGIPriorDayOHLC_Opt(ISeries<double> input, bool showDay, bool showDayPriorVA, bool showCurrentDay, bool showDayCurrentVA, bool showDayVWAP, bool showWeek, bool showWeekPriorVA, bool showCurrentWeek, bool showWeekCurrentVA, bool showWeekVWAP, bool showMonth, bool showMonthPriorVA, bool showCurrentMonth, bool showMonthCurrentVA, bool showMonthVWAP, bool showOvernight, bool showOvernightProfile, TimeSpan overnightStartTime, TimeSpan overnightRTHOpenTime, bool showRTH, bool showOpeningRange, int openingRangeMinutes, bool showInitialBalance, TimeSpan iBStartTime, TimeSpan iBEndTime, double valueAreaPercent, bool profileUseRTHOnly, int ticksPerLevel, double labelOffsetX, double labelOffsetY, double labelFontSize, bool hideBars, bool showProgressPrints)
		{
			if (cacheMGIPriorDayOHLC_Opt != null)
				for (int idx = 0; idx < cacheMGIPriorDayOHLC_Opt.Length; idx++)
					if (cacheMGIPriorDayOHLC_Opt[idx] != null && cacheMGIPriorDayOHLC_Opt[idx].ShowDay == showDay && cacheMGIPriorDayOHLC_Opt[idx].ShowDayPriorVA == showDayPriorVA && cacheMGIPriorDayOHLC_Opt[idx].ShowCurrentDay == showCurrentDay && cacheMGIPriorDayOHLC_Opt[idx].ShowDayCurrentVA == showDayCurrentVA && cacheMGIPriorDayOHLC_Opt[idx].ShowDayVWAP == showDayVWAP && cacheMGIPriorDayOHLC_Opt[idx].ShowWeek == showWeek && cacheMGIPriorDayOHLC_Opt[idx].ShowWeekPriorVA == showWeekPriorVA && cacheMGIPriorDayOHLC_Opt[idx].ShowCurrentWeek == showCurrentWeek && cacheMGIPriorDayOHLC_Opt[idx].ShowWeekCurrentVA == showWeekCurrentVA && cacheMGIPriorDayOHLC_Opt[idx].ShowWeekVWAP == showWeekVWAP && cacheMGIPriorDayOHLC_Opt[idx].ShowMonth == showMonth && cacheMGIPriorDayOHLC_Opt[idx].ShowMonthPriorVA == showMonthPriorVA && cacheMGIPriorDayOHLC_Opt[idx].ShowCurrentMonth == showCurrentMonth && cacheMGIPriorDayOHLC_Opt[idx].ShowMonthCurrentVA == showMonthCurrentVA && cacheMGIPriorDayOHLC_Opt[idx].ShowMonthVWAP == showMonthVWAP && cacheMGIPriorDayOHLC_Opt[idx].ShowOvernight == showOvernight && cacheMGIPriorDayOHLC_Opt[idx].ShowOvernightProfile == showOvernightProfile && cacheMGIPriorDayOHLC_Opt[idx].OvernightStartTime == overnightStartTime && cacheMGIPriorDayOHLC_Opt[idx].OvernightRTHOpenTime == overnightRTHOpenTime && cacheMGIPriorDayOHLC_Opt[idx].ShowRTH == showRTH && cacheMGIPriorDayOHLC_Opt[idx].ShowOpeningRange == showOpeningRange && cacheMGIPriorDayOHLC_Opt[idx].OpeningRangeMinutes == openingRangeMinutes && cacheMGIPriorDayOHLC_Opt[idx].ShowInitialBalance == showInitialBalance && cacheMGIPriorDayOHLC_Opt[idx].IBStartTime == iBStartTime && cacheMGIPriorDayOHLC_Opt[idx].IBEndTime == iBEndTime && cacheMGIPriorDayOHLC_Opt[idx].ValueAreaPercent == valueAreaPercent && cacheMGIPriorDayOHLC_Opt[idx].ProfileUseRTHOnly == profileUseRTHOnly && cacheMGIPriorDayOHLC_Opt[idx].TicksPerLevel == ticksPerLevel && cacheMGIPriorDayOHLC_Opt[idx].LabelOffsetX == labelOffsetX && cacheMGIPriorDayOHLC_Opt[idx].LabelOffsetY == labelOffsetY && cacheMGIPriorDayOHLC_Opt[idx].LabelFontSize == labelFontSize && cacheMGIPriorDayOHLC_Opt[idx].HideBars == hideBars && cacheMGIPriorDayOHLC_Opt[idx].ShowProgressPrints == showProgressPrints && cacheMGIPriorDayOHLC_Opt[idx].EqualsInput(input))
						return cacheMGIPriorDayOHLC_Opt[idx];
			return CacheIndicator<MGIPriorDayOHLC_Opt>(new MGIPriorDayOHLC_Opt(){ ShowDay = showDay, ShowDayPriorVA = showDayPriorVA, ShowCurrentDay = showCurrentDay, ShowDayCurrentVA = showDayCurrentVA, ShowDayVWAP = showDayVWAP, ShowWeek = showWeek, ShowWeekPriorVA = showWeekPriorVA, ShowCurrentWeek = showCurrentWeek, ShowWeekCurrentVA = showWeekCurrentVA, ShowWeekVWAP = showWeekVWAP, ShowMonth = showMonth, ShowMonthPriorVA = showMonthPriorVA, ShowCurrentMonth = showCurrentMonth, ShowMonthCurrentVA = showMonthCurrentVA, ShowMonthVWAP = showMonthVWAP, ShowOvernight = showOvernight, ShowOvernightProfile = showOvernightProfile, OvernightStartTime = overnightStartTime, OvernightRTHOpenTime = overnightRTHOpenTime, ShowRTH = showRTH, ShowOpeningRange = showOpeningRange, OpeningRangeMinutes = openingRangeMinutes, ShowInitialBalance = showInitialBalance, IBStartTime = iBStartTime, IBEndTime = iBEndTime, ValueAreaPercent = valueAreaPercent, ProfileUseRTHOnly = profileUseRTHOnly, TicksPerLevel = ticksPerLevel, LabelOffsetX = labelOffsetX, LabelOffsetY = labelOffsetY, LabelFontSize = labelFontSize, HideBars = hideBars, ShowProgressPrints = showProgressPrints }, input, ref cacheMGIPriorDayOHLC_Opt);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MGIPriorDayOHLC_Opt MGIPriorDayOHLC_Opt(bool showDay, bool showDayPriorVA, bool showCurrentDay, bool showDayCurrentVA, bool showDayVWAP, bool showWeek, bool showWeekPriorVA, bool showCurrentWeek, bool showWeekCurrentVA, bool showWeekVWAP, bool showMonth, bool showMonthPriorVA, bool showCurrentMonth, bool showMonthCurrentVA, bool showMonthVWAP, bool showOvernight, bool showOvernightProfile, TimeSpan overnightStartTime, TimeSpan overnightRTHOpenTime, bool showRTH, bool showOpeningRange, int openingRangeMinutes, bool showInitialBalance, TimeSpan iBStartTime, TimeSpan iBEndTime, double valueAreaPercent, bool profileUseRTHOnly, int ticksPerLevel, double labelOffsetX, double labelOffsetY, double labelFontSize, bool hideBars, bool showProgressPrints)
		{
			return indicator.MGIPriorDayOHLC_Opt(Input, showDay, showDayPriorVA, showCurrentDay, showDayCurrentVA, showDayVWAP, showWeek, showWeekPriorVA, showCurrentWeek, showWeekCurrentVA, showWeekVWAP, showMonth, showMonthPriorVA, showCurrentMonth, showMonthCurrentVA, showMonthVWAP, showOvernight, showOvernightProfile, overnightStartTime, overnightRTHOpenTime, showRTH, showOpeningRange, openingRangeMinutes, showInitialBalance, iBStartTime, iBEndTime, valueAreaPercent, profileUseRTHOnly, ticksPerLevel, labelOffsetX, labelOffsetY, labelFontSize, hideBars, showProgressPrints);
		}

		public Indicators.MGIPriorDayOHLC_Opt MGIPriorDayOHLC_Opt(ISeries<double> input , bool showDay, bool showDayPriorVA, bool showCurrentDay, bool showDayCurrentVA, bool showDayVWAP, bool showWeek, bool showWeekPriorVA, bool showCurrentWeek, bool showWeekCurrentVA, bool showWeekVWAP, bool showMonth, bool showMonthPriorVA, bool showCurrentMonth, bool showMonthCurrentVA, bool showMonthVWAP, bool showOvernight, bool showOvernightProfile, TimeSpan overnightStartTime, TimeSpan overnightRTHOpenTime, bool showRTH, bool showOpeningRange, int openingRangeMinutes, bool showInitialBalance, TimeSpan iBStartTime, TimeSpan iBEndTime, double valueAreaPercent, bool profileUseRTHOnly, int ticksPerLevel, double labelOffsetX, double labelOffsetY, double labelFontSize, bool hideBars, bool showProgressPrints)
		{
			return indicator.MGIPriorDayOHLC_Opt(input, showDay, showDayPriorVA, showCurrentDay, showDayCurrentVA, showDayVWAP, showWeek, showWeekPriorVA, showCurrentWeek, showWeekCurrentVA, showWeekVWAP, showMonth, showMonthPriorVA, showCurrentMonth, showMonthCurrentVA, showMonthVWAP, showOvernight, showOvernightProfile, overnightStartTime, overnightRTHOpenTime, showRTH, showOpeningRange, openingRangeMinutes, showInitialBalance, iBStartTime, iBEndTime, valueAreaPercent, profileUseRTHOnly, ticksPerLevel, labelOffsetX, labelOffsetY, labelFontSize, hideBars, showProgressPrints);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MGIPriorDayOHLC_Opt MGIPriorDayOHLC_Opt(bool showDay, bool showDayPriorVA, bool showCurrentDay, bool showDayCurrentVA, bool showDayVWAP, bool showWeek, bool showWeekPriorVA, bool showCurrentWeek, bool showWeekCurrentVA, bool showWeekVWAP, bool showMonth, bool showMonthPriorVA, bool showCurrentMonth, bool showMonthCurrentVA, bool showMonthVWAP, bool showOvernight, bool showOvernightProfile, TimeSpan overnightStartTime, TimeSpan overnightRTHOpenTime, bool showRTH, bool showOpeningRange, int openingRangeMinutes, bool showInitialBalance, TimeSpan iBStartTime, TimeSpan iBEndTime, double valueAreaPercent, bool profileUseRTHOnly, int ticksPerLevel, double labelOffsetX, double labelOffsetY, double labelFontSize, bool hideBars, bool showProgressPrints)
		{
			return indicator.MGIPriorDayOHLC_Opt(Input, showDay, showDayPriorVA, showCurrentDay, showDayCurrentVA, showDayVWAP, showWeek, showWeekPriorVA, showCurrentWeek, showWeekCurrentVA, showWeekVWAP, showMonth, showMonthPriorVA, showCurrentMonth, showMonthCurrentVA, showMonthVWAP, showOvernight, showOvernightProfile, overnightStartTime, overnightRTHOpenTime, showRTH, showOpeningRange, openingRangeMinutes, showInitialBalance, iBStartTime, iBEndTime, valueAreaPercent, profileUseRTHOnly, ticksPerLevel, labelOffsetX, labelOffsetY, labelFontSize, hideBars, showProgressPrints);
		}

		public Indicators.MGIPriorDayOHLC_Opt MGIPriorDayOHLC_Opt(ISeries<double> input , bool showDay, bool showDayPriorVA, bool showCurrentDay, bool showDayCurrentVA, bool showDayVWAP, bool showWeek, bool showWeekPriorVA, bool showCurrentWeek, bool showWeekCurrentVA, bool showWeekVWAP, bool showMonth, bool showMonthPriorVA, bool showCurrentMonth, bool showMonthCurrentVA, bool showMonthVWAP, bool showOvernight, bool showOvernightProfile, TimeSpan overnightStartTime, TimeSpan overnightRTHOpenTime, bool showRTH, bool showOpeningRange, int openingRangeMinutes, bool showInitialBalance, TimeSpan iBStartTime, TimeSpan iBEndTime, double valueAreaPercent, bool profileUseRTHOnly, int ticksPerLevel, double labelOffsetX, double labelOffsetY, double labelFontSize, bool hideBars, bool showProgressPrints)
		{
			return indicator.MGIPriorDayOHLC_Opt(input, showDay, showDayPriorVA, showCurrentDay, showDayCurrentVA, showDayVWAP, showWeek, showWeekPriorVA, showCurrentWeek, showWeekCurrentVA, showWeekVWAP, showMonth, showMonthPriorVA, showCurrentMonth, showMonthCurrentVA, showMonthVWAP, showOvernight, showOvernightProfile, overnightStartTime, overnightRTHOpenTime, showRTH, showOpeningRange, openingRangeMinutes, showInitialBalance, iBStartTime, iBEndTime, valueAreaPercent, profileUseRTHOnly, ticksPerLevel, labelOffsetX, labelOffsetY, labelFontSize, hideBars, showProgressPrints);
		}
	}
}

#endregion
