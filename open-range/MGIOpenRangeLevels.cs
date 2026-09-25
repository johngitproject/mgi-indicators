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

namespace NinjaTrader.NinjaScript.Indicators
{
    public class MGIOpenRangeLevels : Indicator
    {
        private class ORLLevels
        {
            public DateTime OpenDt;
            public int OpenBarIndex = -1;
            public int EndBarIndex = -1;
            public bool EndWideScanned;   // scan large deja tente (evite O(N) a chaque barre)
            public bool EndSkipPrinted;   // diagnostic "fin introuvable" deja affiche
            public bool HasOR30; public double OR30_H, OR30_L, OR30_M, OR30_Range;
            public double[] OR30_ExtUp = new double[5]; public double[] OR30_ExtDn = new double[5];
            public bool HasOR5; public double OR5_H, OR5_L, OR5_M, OR5_Range;
            public double[] OR5_ExtUp = new double[5]; public double[] OR5_ExtDn = new double[5];
            public bool Precise30; public bool Precise5;
        }

        private ORLLevels _last; // derniere session pour affichage
        private ORLLevels _cur;
        private Dictionary<DateTime, ORLLevels> _hist; // pour backtest historique
        private DateTime _curOpenDt = DateTime.MinValue;
        private double _cur30H = double.MinValue, _cur30L = double.MaxValue;
        private double _cur5H = double.MinValue, _cur5L = double.MaxValue;
        private bool _has30, _has5;
        // cache tri + precise secondaires (leger, sans boucle)
        private List<DateTime> _sortedCache = new List<DateTime>();
        private bool _sortedDirty = true;
        private Dictionary<DateTime, double[]> _precise30; // [H,L] exact 30s via serie Second/30
        private Dictionary<DateTime, double[]> _precise5;  // [H,L] exact 5min via serie Minute/1
        private DateTime _m1AccOpenDt = DateTime.MinValue;
        private double _m1AccH = double.MinValue, _m1AccL = double.MaxValue;
        private int _m1AccCount = 0;
        // menage des objets suffixes par date (evite lignes fantomes si N reduit ou bascule 1<->N)
        private HashSet<string> _drawnSuffixes;
        private string _lastDrawKey = null;
        // telemetrie diagnostique (retirer apres resolution) : 1 print max par evenement/session
        private DateTime _dbgLastSwitchPrint = DateTime.MinValue;
        private int _dbgLastDrawHour = -1;
        private DateTime _dbgLastDrawDay = DateTime.MinValue;
        private DateTime _dbgLastRejectPrint = DateTime.MinValue;
        private DateTime _dbgLastGuardPrintDay = DateTime.MinValue;

        public override string DisplayName => Name;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"MGI Open Range Levels - OR 30s (Second/30 precis) + OR 5min (Minute/1 precis), fallback serie primaire. Lignes des 14h30, N sessions, Values pour backtest.";
                Name = "MGIOpenRangeLevels";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = true;
                PaintPriceMarkers = false;
                IsAutoScale = false;
                BarsRequiredToPlot = 2;
                OpenTime = new TimeSpan(14, 30, 0); // REFERENCE ETE (DST) en heure chart fixe : 9:30 ET en ete. +1h auto en hiver si AdjustForUsDst.
                EndTime = new TimeSpan(21, 0, 0);
                AdjustForUsDst = true; // DST US auto : +1h en hiver (EST). false = heures saisies toute l'annee.
                ExtendOvernight = false;
                NumExtensions = 4;
                HistoricalSessions = 1;
                // Series secondaires legeres pour precision 30s/5min independante du timeframe :
                // Second/30 = 1 bougie = exact OR30 ; Minute/1 = 5 bougies = exact OR5. ~1170 bar/jour, pas de crash.
                try
                {
                    AddDataSeries(BarsPeriodType.Second, 30);
                    AddDataSeries(BarsPeriodType.Minute, 1);
                } catch {}
                ShowOR30Level = true; ShowOR30Extensions = true;
                OR30HighColor = Brushes.Orange; OR30LowColor = Brushes.DodgerBlue; OR30MidColor = Brushes.Gold;
                OR30ExtUpColor = Brushes.LimeGreen; OR30ExtDnColor = Brushes.IndianRed;
                OR30LineWidth = 1; OR30TextSize = 9;
                ShowOR5Level = true; ShowOR5Extensions = true;
                OR5HighColor = Brushes.DeepSkyBlue; OR5LowColor = Brushes.Violet; OR5MidColor = Brushes.White;
                OR5ExtUpColor = Brushes.Lime; OR5ExtDnColor = Brushes.OrangeRed;
                OR5LineWidth = 1; OR5TextSize = 9;
                AddPlot(new Stroke(Brushes.Orange, 2), PlotStyle.Line, "OR30_H");
                AddPlot(new Stroke(Brushes.DodgerBlue, 2), PlotStyle.Line, "OR30_L");
                AddPlot(new Stroke(Brushes.Gold, 2), PlotStyle.Line, "OR30_M");
                AddPlot(new Stroke(Brushes.LimeGreen, 1), PlotStyle.Line, "OR30_Ext1Up");
                AddPlot(new Stroke(Brushes.LimeGreen, 1), PlotStyle.Line, "OR30_Ext2Up");
                AddPlot(new Stroke(Brushes.LimeGreen, 1), PlotStyle.Line, "OR30_Ext3Up");
                AddPlot(new Stroke(Brushes.LimeGreen, 1), PlotStyle.Line, "OR30_Ext4Up");
                AddPlot(new Stroke(Brushes.LimeGreen, 1), PlotStyle.Line, "OR30_Ext5Up");
                AddPlot(new Stroke(Brushes.IndianRed, 1), PlotStyle.Line, "OR30_Ext1Dn");
                AddPlot(new Stroke(Brushes.IndianRed, 1), PlotStyle.Line, "OR30_Ext2Dn");
                AddPlot(new Stroke(Brushes.IndianRed, 1), PlotStyle.Line, "OR30_Ext3Dn");
                AddPlot(new Stroke(Brushes.IndianRed, 1), PlotStyle.Line, "OR30_Ext4Dn");
                AddPlot(new Stroke(Brushes.IndianRed, 1), PlotStyle.Line, "OR30_Ext5Dn");
                AddPlot(new Stroke(Brushes.DeepSkyBlue, 2), PlotStyle.Line, "OR5_H");
                AddPlot(new Stroke(Brushes.Violet, 2), PlotStyle.Line, "OR5_L");
                AddPlot(new Stroke(Brushes.White, 2), PlotStyle.Line, "OR5_M");
                AddPlot(new Stroke(Brushes.Lime, 1), PlotStyle.Line, "OR5_Ext1Up");
                AddPlot(new Stroke(Brushes.Lime, 1), PlotStyle.Line, "OR5_Ext2Up");
                AddPlot(new Stroke(Brushes.Lime, 1), PlotStyle.Line, "OR5_Ext3Up");
                AddPlot(new Stroke(Brushes.Lime, 1), PlotStyle.Line, "OR5_Ext4Up");
                AddPlot(new Stroke(Brushes.Lime, 1), PlotStyle.Line, "OR5_Ext5Up");
                AddPlot(new Stroke(Brushes.OrangeRed, 1), PlotStyle.Line, "OR5_Ext1Dn");
                AddPlot(new Stroke(Brushes.OrangeRed, 1), PlotStyle.Line, "OR5_Ext2Dn");
                AddPlot(new Stroke(Brushes.OrangeRed, 1), PlotStyle.Line, "OR5_Ext3Dn");
                AddPlot(new Stroke(Brushes.OrangeRed, 1), PlotStyle.Line, "OR5_Ext4Dn");
                AddPlot(new Stroke(Brushes.OrangeRed, 1), PlotStyle.Line, "OR5_Ext5Dn");
            }
            else if (State == State.DataLoaded)
            {
                _last = null; _cur = null; _hist = new Dictionary<DateTime, ORLLevels>();
                _precise30 = new Dictionary<DateTime, double[]>();
                _precise5 = new Dictionary<DateTime, double[]>();
                if (_sortedCache == null) _sortedCache = new List<DateTime>(); else _sortedCache.Clear();
                _sortedDirty = true;
                if (_drawnSuffixes == null) _drawnSuffixes = new HashSet<string>(); else _drawnSuffixes.Clear();
                _lastDrawKey = null;
                _curOpenDt = DateTime.MinValue;
                _cur30H = double.MinValue; _cur30L = double.MaxValue;
                _cur5H = double.MinValue; _cur5L = double.MaxValue;
                _has30 = _has5 = false;
                _m1AccOpenDt = DateTime.MinValue; _m1AccH = double.MinValue; _m1AccL = double.MaxValue; _m1AccCount = 0;
                try { Print("ORL build 2026-09-09f-ENDIDX (fin future = index absolu courant, plus jamais 0) charge : " + Instrument.FullName + " " + BarsPeriod.Value + BarsPeriod.BarsPeriodType + " OpenTime=" + OpenTime + " EndTime=" + EndTime + " AdjustForUsDst=" + AdjustForUsDst + " TH=" + (Bars.TradingHours != null ? Bars.TradingHours.Name : "?")); } catch {}
                _dbgLastSwitchPrint = DateTime.MinValue; _dbgLastDrawHour = -1; _dbgLastDrawDay = DateTime.MinValue;
                _dbgLastRejectPrint = DateTime.MinValue; _dbgLastGuardPrintDay = DateTime.MinValue;
                // Plots invisibles pour affichage (on trace via Draw.Text uniquement), mais Values gardes pour backtest
                try { for(int i=0;i<26;i++){ Plots[i].Brush = Brushes.Transparent; Plots[i].Width = 1; } } catch {}
            }
            else if (State == State.Terminated)
            {
                _last = null; _cur = null; if(_hist!=null) _hist.Clear();
                if(_precise30!=null) _precise30.Clear(); if(_precise5!=null) _precise5.Clear();
                if(_sortedCache!=null) _sortedCache.Clear(); _sortedDirty = true;
                if(_drawnSuffixes!=null) _drawnSuffixes.Clear(); _lastDrawKey = null;
            }
        }

        private TimeSpan GetEffOpen(DateTime day)
        {
            return MgiSessionClock.DstAdjusted(OpenTime, day, AdjustForUsDst);
        }
        private DateTime GetOpenDt(DateTime barOpenTime)
        {
            TimeSpan effOpen = GetEffOpen(barOpenTime.Date);
            DateTime c = barOpenTime.Date + effOpen;
            if (barOpenTime < c) c = c.AddDays(-1);
            return c;
        }
        private DateTime GetEndDt(DateTime openDt)
        {
            TimeSpan effEnd = MgiSessionClock.DstAdjusted(EndTime, openDt.Date, AdjustForUsDst);
            DateTime e = openDt.Date + effEnd;
            if (e <= openDt) e = e.AddDays(1);
            return e;
        }
        private DateTime GetNextSessionOpen(DateTime after)
        {
            // Vrai open de la journee suivante : 1ere session connue strictement apres 'after'
            // (lundi apres vendredi, fériés et DST automatiquement exacts). MaxValue si aucune.
            try
            {
                List<DateTime> all = GetSortedSessions();
                if (all != null) for (int i = 0; i < all.Count; i++) if (all[i] > after) return all[i];
            } catch {}
            return DateTime.MaxValue;
        }
        private DateTime GetEndDtDisplay(DateTime openDt, DateTime nextOpenDt)
        {
            // Affichage uniquement (backtest/Values non touches) : en overnight, la fin = l'open
            // de la journee suivante ; si encore inconnu (derniere session) -> MaxValue = croissance
            // jusqu'a maintenant, figee au prochain open des sa formation.
            if (ExtendOvernight)
            {
                if (nextOpenDt != DateTime.MinValue && nextOpenDt != DateTime.MaxValue && nextOpenDt > openDt)
                    return nextOpenDt;
                return DateTime.MaxValue;
            }
            return GetEndDt(openDt);
        }
        // CONVENTION CHART : l'heure affichee par chaque bougie = son CLOSE.
        // Donc open = close - duree (TF a duree fixe), close = Time[0] direct.
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
                    default: return close; // Tick/Range/Volume/Renko : pas de duree fixe, open ~= close (evenement)
                }
            } catch { return close; }
        }
        private DateTime GetBarOpenTime()
        {
            try { return SubtractPeriod(Times[0][0]); } catch { return DateTime.MinValue; }
        }
        // R1 : serie primaire explicite (BarsArray[0]/Times[0]/CurrentBars[0]) partout, jamais les
        // formes non qualifiees (Time[], CurrentBar, Count) qui peuvent suivre une autre serie.
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
        private DateTime GetBarOpenTime(int absoluteIdx)
        {
            // idx = INDEX ABSOLU (0 = premiere bougie). Time[] est indexe en barsAgo : conversion obligatoire.
            int pc = PCount(); int cur = PCur();
            if (pc == 0) return DateTime.MinValue;
            if (absoluteIdx < 0) absoluteIdx = 0; if (absoluteIdx >= pc) absoluteIdx = pc - 1;
            int barsAgo = cur - absoluteIdx;
            if (barsAgo < 0) barsAgo = 0; if (barsAgo >= pc) barsAgo = pc - 1;
            try { return SubtractPeriod(PTime(barsAgo)); } catch { return DateTime.MinValue; }
        }
        private DateTime GetBarOpenTimeByBarsAgo(int barsAgo)
        {
            int pc = PCount();
            if (pc == 0) return DateTime.MinValue;
            if (barsAgo < 0) barsAgo = 0; if (barsAgo >= pc) barsAgo = pc - 1;
            try { return SubtractPeriod(PTime(barsAgo)); } catch { return DateTime.MinValue; }
        }
        private DateTime GetBarCloseTime()
        {
            try { return PTime(0); } catch { return DateTime.MinValue; }
        }
        private int FindFirstBarAtOrAfter(DateTime time)
        {
            // PREMIERE bougie dont l'open >= time (pas la bougie contenant time : evite 12h sur TF 4h).
            // Retourne un INDEX ABSOLU. Comparaison sur l'open calcule (close - duree), car le stamp
            // affiche = close. Borne a 3000 bougies ; les sessions _hist utilisent leur cache de toute facon.
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
            } catch {}
            return -1;
        }
        private int FindBarIndexForTime(DateTime time)
        {
            // garde pour compat : redirige vers FirstAtOrAfter (demarrage exact 14h30)
            return FindFirstBarAtOrAfter(time);
        }
        private int FindFirstBarAtOrAfterWide(DateTime time)
        {
            // Scan pleine histoire (sans borne 3000), utilise UNE fois puis memoise via EndWideScanned.
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
            } catch {}
            return -1;
        }
        private bool ValidStartIdx(int absIdx, DateTime openDt)
        {
            // R3 : un index de demarrage n'est reutilise que si l'open lu a cet index est
            // coherent (1ere bougie ouvrant a/apres openDt, tolerance large pour gros TF).
            try
            {
                int cur = PCur();
                if (absIdx < 0 || absIdx > cur) return false;
                DateTime o = GetBarOpenTime(absIdx);
                if (o == DateTime.MinValue) return false;
                return o >= openDt && o < openDt.AddDays(7);
            } catch { return false; }
        }
        private int ResolveEndBarIndex(ORLLevels lvl, DateTime endDt)
        {
            // Retourne un INDEX ABSOLU : la 1ere bougie ouvrant a/apres endDt, ou l'index absolu
            // de la bougie courante si fin future (session en cours : ligne "jusqu'a maintenant"
            // -> eBars = 0), ou -1 si fin passee irresolvable -> l'appelant NE DESSINE PAS.
            // (0 = premiere bougie du chart, JAMAIS "maintenant" : ne pas le retourner ici.)
            try
            {
                DateTime now;
                try { now = PTime(0); } catch { return -1; }
                if (endDt >= now) return PCur();
                int cur = PCur();
                if (lvl.EndBarIndex >= 0 && lvl.EndBarIndex <= cur)
                {
                    // R3 : valide le cache (sinon un index empoisonne colle a jamais)
                    try
                    {
                        DateTime o = GetBarOpenTime(lvl.EndBarIndex);
                        if (o != DateTime.MinValue && o >= endDt && o < endDt.AddDays(4)) return lvl.EndBarIndex;
                    } catch {}
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
                    try { Print("ORL skip " + lvl.OpenDt.ToString("yyyy-MM-dd") + " : fin " + endDt.ToString("HH:mm") + " introuvable (session non dessinee)"); } catch {}
                }
            } catch {}
            return -1;
        }
        private bool CheckDrawInvariant(int sBars, int eBars, int cur, string where)
        {
            // R2 : jamais de segment degenere (longueur nulle), inverse ou hors bornes.
            if (eBars >= 0 && sBars > eBars && sBars <= cur) return true;
            try
            {
                DateTime today;
                try { today = PTime(0).Date; } catch { today = DateTime.Today; }
                if (_dbgLastGuardPrintDay != today)
                {
                    _dbgLastGuardPrintDay = today;
                    Print("ORL garde dessin " + where + " : sBars=" + sBars + " eBars=" + eBars + " cur=" + cur + " (dessin ignore)");
                }
            } catch {}
            return false;
        }
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
            } catch {}
            return _sortedCache;
        }
        private void DrawORLevel(string tag, string label, int barsAgoStart, double price, Brush brush, DashStyleHelper dash, int width, int textSize)
        {
            if (double.IsNaN(price) || double.IsInfinity(price)) { RemoveDrawObject(tag); RemoveDrawObject(tag+"_Lbl"); return; }
            if (ChartControl == null) return; // hébergé en stratégie : pas de chart, Draw retourne null
            var line = Draw.Line(this, tag, barsAgoStart, price, 0, price, brush);
            if (line == null) return;
            line.Stroke = new Stroke(brush, dash, width);
            try { Draw.Text(this, tag+"_Lbl", false, label, 0, price, 5, brush, new Gui.Tools.SimpleFont("Arial", (float)textSize), TextAlignment.Left, null, null, 0); } catch {}
        }
        private void DrawORLevelHist(string tag, string label, int startBarsAgo, int endBarsAgo, double price, Brush brush, DashStyleHelper dash, int width, int textSize)
        {
            if (double.IsNaN(price) || double.IsInfinity(price)) { RemoveDrawObject(tag); RemoveDrawObject(tag+"_Lbl"); return; }
            if (ChartControl == null) return;
            if (startBarsAgo < endBarsAgo) { int t=startBarsAgo; startBarsAgo=endBarsAgo; endBarsAgo=t; }
            if (endBarsAgo < 0) endBarsAgo = 0;
            var line = Draw.Line(this, tag, startBarsAgo, price, endBarsAgo, price, brush);
            if (line == null) return;
            line.Stroke = new Stroke(brush, dash, width);
            try { Draw.Text(this, tag+"_Lbl", false, label, endBarsAgo, price, 5, brush, new Gui.Tools.SimpleFont("Arial", (float)textSize), TextAlignment.Left, null, null, 0); } catch {}
        }
        private void DrawORLevelTime(string tag, string label, DateTime startTime, DateTime endTime, double price, Brush brush, DashStyleHelper dash, int width, int textSize)
        {
            if (double.IsNaN(price) || double.IsInfinity(price)) { RemoveDrawObject(tag); RemoveDrawObject(tag+"_Lbl"); return; }
            if (ChartControl == null) return;
            try
            {
                int cur = PCur();
                int sIdx = FindBarIndexForTime(startTime);
                int eIdx = FindBarIndexForTime(endTime);
                if (sIdx < 0) sIdx = cur;
                if (eIdx < 0) eIdx = cur;
                int sBars = cur - sIdx;
                int eBars = cur - eIdx;
                if (sBars < 0) sBars = 0;
                if (eBars < 0) eBars = 0;
                var line = Draw.Line(this, tag, sBars, price, eBars, price, brush);
                if (line == null) return;
                line.Stroke = new Stroke(brush, dash, width);
                Draw.Text(this, tag+"_Lbl", false, label, eBars, price, 5, brush, new Gui.Tools.SimpleFont("Arial", (float)textSize), TextAlignment.Left, null, null, 0);
            } catch { try { DrawORLevel(tag, label, 0, price, brush, dash, width, textSize); } catch {} }
        }
        private void DrawORLevel(string tag, string label, int barsAgoStart, double price, Brush brush, DashStyleHelper dash, int width)
        {
            DrawORLevel(tag, label, barsAgoStart, price, brush, dash, width, 9);
        }
        private void RemoveORLevel(string tag) { RemoveDrawObject(tag); RemoveDrawObject(tag+"_Lbl"); }
        private void RemoveSessionDraws(string suffix)
        {
            RemoveORLevel("OR30_H_"+suffix); RemoveORLevel("OR30_L_"+suffix); RemoveORLevel("OR30_M_"+suffix);
            RemoveORLevel("OR5_H_"+suffix); RemoveORLevel("OR5_L_"+suffix); RemoveORLevel("OR5_M_"+suffix);
            for(int i=0;i<5;i++)
            {
                RemoveORLevel($"OR30_Ext{i+1}Up_"+suffix); RemoveORLevel($"OR30_Ext{i+1}Dn_"+suffix);
                RemoveORLevel($"OR5_Ext{i+1}Up_"+suffix); RemoveORLevel($"OR5_Ext{i+1}Dn_"+suffix);
            }
        }

        private DateTime GetSecOpenTime(int bip, int barsAgo)
        {
            // Meme convention : Times[bip] = close des bougies secondaires -> open = close - duree.
            try
            {
                DateTime close = Times[bip][barsAgo];
                if (bip == 1) return close.AddSeconds(-30);
                if (bip == 2) return close.AddMinutes(-1);
                return close;
            } catch { return DateTime.MinValue; }
        }
        private void CaptureSec30()
        {
            // BIP1 : 1 bougie Second/30 = exact OR30 si son open == openDt
            try
            {
                if (CurrentBars[1] < 1) return;
                DateTime secOpen = GetSecOpenTime(1, 0);
                if (secOpen == DateTime.MinValue) return;
                DateTime openDt = GetOpenDt(secOpen);
                // ne garde que la bougie qui demarre exactement a l'open (tolerance 1s)
                if (Math.Abs((secOpen - openDt).TotalSeconds) > 1) return;
                double h = Highs[1][0], l = Lows[1][0];
                if (double.IsNaN(h) || double.IsNaN(l)) return;
                if (_precise30 == null) _precise30 = new Dictionary<DateTime, double[]>();
                _precise30[openDt] = new double[] { h, l };
                // si session deja en cours dans _hist, mets a jour retroactivement (secondaire peut arriver apres primaire)
                try
                {
                    ORLLevels lvl = null;
                    if (_hist != null && _hist.TryGetValue(openDt, out lvl) && lvl != null && lvl.HasOR30)
                    {
                        lvl.OR30_H = h; lvl.OR30_L = l; lvl.OR30_M = (h + l) * 0.5; lvl.OR30_Range = h - l;
                        for (int i = 0; i < 5; i++) { lvl.OR30_ExtUp[i] = h + (i + 1) * lvl.OR30_Range; lvl.OR30_ExtDn[i] = l - (i + 1) * lvl.OR30_Range; }
                        lvl.Precise30 = true;
                    }
                    if (_cur != null && _cur.OpenDt == openDt && _cur.HasOR30 && !_cur.Precise30)
                    {
                        _cur.OR30_H = h; _cur.OR30_L = l; _cur.OR30_M = (h + l) * 0.5; _cur.OR30_Range = h - l;
                        for (int i = 0; i < 5; i++) { _cur.OR30_ExtUp[i] = h + (i + 1) * _cur.OR30_Range; _cur.OR30_ExtDn[i] = l - (i + 1) * _cur.OR30_Range; }
                        _cur.Precise30 = true;
                    }
                } catch {}
            } catch {}
        }
        private void CaptureMin1()
        {
            // BIP2 : 5 bougies Minute/1 accumulees = exact OR5
            try
            {
                if (CurrentBars[2] < 1) return;
                DateTime secOpen = GetSecOpenTime(2, 0);
                if (secOpen == DateTime.MinValue) return;
                DateTime openDt = GetOpenDt(secOpen);
                if (secOpen < openDt || secOpen >= openDt.AddMinutes(5)) return;
                double h = Highs[2][0], l = Lows[2][0];
                if (double.IsNaN(h) || double.IsNaN(l)) return;
                if (_m1AccOpenDt != openDt) { _m1AccOpenDt = openDt; _m1AccH = h; _m1AccL = l; _m1AccCount = 1; }
                else
                {
                    if (h > _m1AccH) _m1AccH = h;
                    if (l < _m1AccL) _m1AccL = l;
                    _m1AccCount++;
                }
                if (_m1AccCount >= 5)
                {
                    if (_precise5 == null) _precise5 = new Dictionary<DateTime, double[]>();
                    _precise5[openDt] = new double[] { _m1AccH, _m1AccL };
                    try
                    {
                        ORLLevels lvl = null;
                        if (_hist != null && _hist.TryGetValue(openDt, out lvl) && lvl != null && lvl.HasOR5)
                        {
                            lvl.OR5_H = _m1AccH; lvl.OR5_L = _m1AccL; lvl.OR5_M = (_m1AccH + _m1AccL) * 0.5; lvl.OR5_Range = _m1AccH - _m1AccL;
                            for (int i = 0; i < 5; i++) { lvl.OR5_ExtUp[i] = lvl.OR5_H + (i + 1) * lvl.OR5_Range; lvl.OR5_ExtDn[i] = lvl.OR5_L - (i + 1) * lvl.OR5_Range; }
                            lvl.Precise5 = true;
                        }
                    } catch {}
                }
            } catch {}
        }

        protected override void OnBarUpdate()
        {
            try
            {
                if (BarsInProgress == 1) { CaptureSec30(); return; }
                if (BarsInProgress == 2) { CaptureMin1(); return; }
                if (BarsInProgress != 0) return;
                int pCur = PCur(); int pCount = PCount();
                if (pCur < 1 || pCount == 0) return;

                DateTime barOpenTime = GetBarOpenTime();
                DateTime barCloseTime = GetBarCloseTime();
                DateTime openDt = GetOpenDt(barOpenTime);

                // nouveau jour -> on fige la session precedente comme "derniere disponible"
                if (_curOpenDt == DateTime.MinValue) _curOpenDt = openDt;
                else if (openDt != _curOpenDt)
                {
                    // R4 : switch sanitaire - rejette les rattachements incoherents (callback desync
                    // multi-series pendant le replay) : jamais en arriere, jamais une session future.
                    // Cas legitimes (week-ends, feries) vont toujours vers l'avant : non affectes.
                    if (openDt < _curOpenDt || openDt > barCloseTime)
                    {
                        try
                        {
                            if (_dbgLastRejectPrint != openDt)
                            {
                                _dbgLastRejectPrint = openDt;
                                Print("ORL switch REJETE stamp=" + barCloseTime.ToString("yyyy-MM-dd HH:mm") + " openDt=" + openDt.ToString("yyyy-MM-dd HH:mm") + " cur=" + _curOpenDt.ToString("yyyy-MM-dd HH:mm"));
                            }
                        } catch {}
                        return;
                    }
                    if (_cur != null && (_cur.HasOR30 || _cur.HasOR5))
                        _last = _cur;
                    _cur = new ORLLevels { OpenDt = openDt, OpenBarIndex = pCur };
                    _curOpenDt = openDt;
                    _cur30H = double.MinValue; _cur30L = double.MaxValue; _has30 = false;
                    _cur5H = double.MinValue; _cur5L = double.MaxValue; _has5 = false;
                }
                // DIAG : 1 print par session - rattachement observe
                try
                {
                    if (openDt != _dbgLastSwitchPrint)
                    {
                        _dbgLastSwitchPrint = openDt;
                        Print("ORL switch stamp=" + barCloseTime.ToString("yyyy-MM-dd HH:mm") + " barOpen=" + barOpenTime.ToString("HH:mm") + " effOpen=" + GetEffOpen(barOpenTime.Date) + " openDt=" + openDt.ToString("yyyy-MM-dd HH:mm") + " last=" + (_last != null ? _last.OpenDt.ToString("yyyy-MM-dd HH:mm") : "null"));
                    }
                } catch {}
                if (_cur == null) _cur = new ORLLevels { OpenDt = openDt, OpenBarIndex = pCur };
                if (_cur.OpenBarIndex < 0) _cur.OpenBarIndex = pCur;

                bool in30 = barOpenTime < openDt.AddSeconds(30) && barCloseTime > openDt;
                bool in5 = barOpenTime < openDt.AddMinutes(5) && barCloseTime > openDt;
                if (in30) { double h=Highs[0][0], l=Lows[0][0]; if (!double.IsNaN(h)&&!double.IsNaN(l)){ if(_cur30H==double.MinValue){_cur30H=h;_cur30L=l;} else{ if(h>_cur30H)_cur30H=h; if(l<_cur30L)_cur30L=l; } } }
                if (in5) { double h=Highs[0][0], l=Lows[0][0]; if (!double.IsNaN(h)&&!double.IsNaN(l)){ if(_cur5H==double.MinValue){_cur5H=h;_cur5L=l;} else{ if(h>_cur5H)_cur5H=h; if(l<_cur5L)_cur5L=l; } } }

                if (!_has30 && _cur30H != double.MinValue && barCloseTime >= openDt.AddSeconds(30))
                {
                    // priorite au precis secondaire (exact 30s), fallback primaire
                    double pH = _cur30H, pL = _cur30L; bool prec = false;
                    try
                    {
                        double[] pr = null;
                        if (_precise30 != null && _precise30.TryGetValue(openDt, out pr) && pr != null) { pH = pr[0]; pL = pr[1]; prec = true; }
                    } catch {}
                    _cur.HasOR30=true; _cur.OR30_H=pH; _cur.OR30_L=pL; _cur.OR30_M=(pH+pL)*0.5; _cur.OR30_Range=pH-pL;
                    for(int i=0;i<5;i++){ _cur.OR30_ExtUp[i]=_cur.OR30_H+(i+1)*_cur.OR30_Range; _cur.OR30_ExtDn[i]=_cur.OR30_L-(i+1)*_cur.OR30_Range; }
                    _cur.Precise30 = prec;
                    _has30=true;
                    _last = _cur; _hist[openDt]=_cur; MarkSortedDirty();
                    try { Print("ORL OR30 fige " + openDt.ToString("yyyy-MM-dd HH:mm") + " H=" + pH + " L=" + pL + " prec=" + prec + " a " + barCloseTime.ToString("HH:mm")); } catch {}
                }
                if (!_has5 && _cur5H != double.MinValue && barCloseTime >= openDt.AddMinutes(5))
                {
                    double pH = _cur5H, pL = _cur5L; bool prec = false;
                    try
                    {
                        double[] pr = null;
                        if (_precise5 != null && _precise5.TryGetValue(openDt, out pr) && pr != null) { pH = pr[0]; pL = pr[1]; prec = true; }
                        else if (_m1AccOpenDt == openDt && _m1AccCount > 0 && _m1AccH != double.MinValue) { pH = _m1AccH; pL = _m1AccL; prec = true; }
                    } catch {}
                    _cur.HasOR5=true; _cur.OR5_H=pH; _cur.OR5_L=pL; _cur.OR5_M=(pH+pL)*0.5; _cur.OR5_Range=pH-pL;
                    for(int i=0;i<5;i++){ _cur.OR5_ExtUp[i]=_cur.OR5_H+(i+1)*_cur.OR5_Range; _cur.OR5_ExtDn[i]=_cur.OR5_L-(i+1)*_cur.OR5_Range; }
                    _cur.Precise5 = prec;
                    _has5=true;
                    _last = _cur; _hist[openDt]=_cur; MarkSortedDirty();
                    try { Print("ORL OR5 fige " + openDt.ToString("yyyy-MM-dd HH:mm") + " H=" + pH + " L=" + pL + " prec=" + prec + " a " + barCloseTime.ToString("HH:mm")); } catch {}
                }
                if (_cur != null && (_cur.HasOR30 || _cur.HasOR5)) { _last=_cur; if(!_hist.ContainsKey(openDt)) MarkSortedDirty(); _hist[openDt]=_cur; }

                // backtest : Values TOUJOURS renseignes (Show = affichage seul, pas de masquage strategie).
                // Seul NumExtensions limite les Values d'extension (parametre metier).
                ORLLevels histLvl = null; _hist.TryGetValue(openDt, out histLvl);
                if (histLvl == null) { for(int i=0;i<26;i++) Values[i][0]=double.NaN; }
                else
                {
                    bool h30 = histLvl.HasOR30; bool h5 = histLvl.HasOR5;
                    Values[0][0]=h30?histLvl.OR30_H:double.NaN; Values[1][0]=h30?histLvl.OR30_L:double.NaN; Values[2][0]=h30?histLvl.OR30_M:double.NaN;
                    for(int i=0;i<5;i++){ Values[3+i][0]=h30?histLvl.OR30_ExtUp[i]:double.NaN; Values[8+i][0]=h30?histLvl.OR30_ExtDn[i]:double.NaN; }
                    Values[13][0]=h5?histLvl.OR5_H:double.NaN; Values[14][0]=h5?histLvl.OR5_L:double.NaN; Values[15][0]=h5?histLvl.OR5_M:double.NaN;
                    for(int i=0;i<5;i++){ Values[16+i][0]=h5?histLvl.OR5_ExtUp[i]:double.NaN; Values[21+i][0]=h5?histLvl.OR5_ExtDn[i]:double.NaN; }
                    for(int i=NumExtensions;i<5;i++){ Values[3+i][0]=double.NaN; Values[8+i][0]=double.NaN; Values[16+i][0]=double.NaN; Values[21+i][0]=double.NaN; }
                }

                // affichage : dessine a chaque barre primaire (cout faible via index caches).
                // Pas de porte allowDraw/inDisplayWindow : la derniere barre peut etre hors fenetre
                // (apres 21h) mais le segment [open->end] doit rester visible.
                int nHist = Math.Max(1, HistoricalSessions);
                List<DateTime> sorted = GetSortedSessions();
                if (sorted == null || sorted.Count == 0) return;
                int startIdx = Math.Max(0, sorted.Count - nHist);
                // menage (1x par changement de fenetre) : supprime les sessions suffixees sorties de la
                // fenetre d'affichage, sinon leurs lignes restent collees sur d'anciennes dates.
                try
                {
                    string drawKey = nHist + "|" + startIdx + "|" + sorted.Count + "|" + sorted[sorted.Count-1].Ticks;
                    if (drawKey != _lastDrawKey)
                    {
                        _lastDrawKey = drawKey;
                        HashSet<string> wanted = new HashSet<string>();
                        if (nHist > 1) for (int w = startIdx; w < sorted.Count; w++) wanted.Add(sorted[w].ToString("yyyyMMdd"));
                        if (_drawnSuffixes != null)
                        {
                            foreach (string suf in new List<string>(_drawnSuffixes))
                                if (!wanted.Contains(suf)) { RemoveSessionDraws(suf); _drawnSuffixes.Remove(suf); }
                        }
                        if (_drawnSuffixes == null) _drawnSuffixes = new HashSet<string>();
                        foreach (string w in wanted) _drawnSuffixes.Add(w);
                    }
                } catch {}
                if (nHist == 1)
                {
                    if (_last == null) return;
                    bool show30 = ShowOR30Level && _last.HasOR30;
                    bool show30Ext = ShowOR30Extensions && _last.HasOR30;
                    bool show5 = ShowOR5Level && _last.HasOR5;
                    bool show5Ext = ShowOR5Extensions && _last.HasOR5;
                    // overnight : fin = open de la journee suivante (lundi apres vendredi) ; affichage seul
                    DateTime displayEnd2 = GetEndDtDisplay(_last.OpenDt, GetNextSessionOpen(_last.OpenDt));
                    if (PTime(0) < displayEnd2) displayEnd2 = PTime(0);
                    // CORRECTIF 14h30 : 1ere bougie >= open. R3 : le refind est transient
                    // (jamais stocke) et le cache n'est reutilise que si valide.
                    int cur1 = PCur();
                    int startIdx2 = ValidStartIdx(_last.OpenBarIndex, _last.OpenDt) ? _last.OpenBarIndex : FindFirstBarAtOrAfter(_last.OpenDt);
                    if (startIdx2 < 0 || startIdx2 > cur1 || !ValidStartIdx(startIdx2, _last.OpenDt)) return; // debut incoherent : ne dessine pas
                    int endIdx2 = ResolveEndBarIndex(_last, displayEnd2);
                    if (endIdx2 < 0) return; // fin passee irresolvable : ne dessine pas (jamais jusqu'au bout du chart)
                    int sBars = cur1 - startIdx2;
                    int eBars = cur1 - endIdx2;
                    if (!CheckDrawInvariant(sBars, eBars, cur1, "N1")) return;
                    // DIAG : 1 print par heure - ce qui est dessine en N=1
                    try
                    {
                        if (_dbgLastDrawDay != _last.OpenDt.Date || _dbgLastDrawHour != PTime(0).Hour)
                        {
                            _dbgLastDrawDay = _last.OpenDt.Date; _dbgLastDrawHour = PTime(0).Hour;
                            Print("ORL drawN1 last=" + _last.OpenDt.ToString("yyyy-MM-dd HH:mm") + " sBars=" + sBars + " eBars=" + eBars + " OR30H=" + _last.OR30_H + " OR5H=" + _last.OR5_H + " a " + PTime(0).ToString("yyyy-MM-dd HH:mm"));
                        }
                    } catch {}
                    if (show30)
                    {
                        DrawORLevelHist("OR30_H","OR30_H", sBars, eBars, _last.OR30_H, OR30HighColor, DashStyleHelper.Solid, OR30LineWidth, OR30TextSize);
                        DrawORLevelHist("OR30_L","OR30_L", sBars, eBars, _last.OR30_L, OR30LowColor, DashStyleHelper.Solid, OR30LineWidth, OR30TextSize);
                        DrawORLevelHist("OR30_M","OR30_M", sBars, eBars, _last.OR30_M, OR30MidColor, DashStyleHelper.Dash, OR30LineWidth, OR30TextSize);
                    } else { RemoveORLevel("OR30_H"); RemoveORLevel("OR30_L"); RemoveORLevel("OR30_M"); }
                    if (show30Ext)
                    {
                        for(int i=0;i<NumExtensions;i++){
                            DrawORLevelHist($"OR30_Ext{i+1}Up",$"OR30 Ext{i+1} Up", sBars, eBars, _last.OR30_ExtUp[i], OR30ExtUpColor, DashStyleHelper.Dash, OR30LineWidth, OR30TextSize);
                            DrawORLevelHist($"OR30_Ext{i+1}Dn",$"OR30 Ext{i+1} Dn", sBars, eBars, _last.OR30_ExtDn[i], OR30ExtDnColor, DashStyleHelper.Dash, OR30LineWidth, OR30TextSize);
                        }
                    }
                    for(int i=(show30Ext?NumExtensions:0);i<5;i++){ RemoveORLevel($"OR30_Ext{i+1}Up"); RemoveORLevel($"OR30_Ext{i+1}Dn"); }
                    if (!show30) { for(int i=0;i<5;i++){ RemoveORLevel($"OR30_Ext{i+1}Up"); RemoveORLevel($"OR30_Ext{i+1}Dn"); } }
                    if (show5)
                    {
                        DrawORLevelHist("OR5_H","OR5_H", sBars, eBars, _last.OR5_H, OR5HighColor, DashStyleHelper.Solid, OR5LineWidth, OR5TextSize);
                        DrawORLevelHist("OR5_L","OR5_L", sBars, eBars, _last.OR5_L, OR5LowColor, DashStyleHelper.Solid, OR5LineWidth, OR5TextSize);
                        DrawORLevelHist("OR5_M","OR5_M", sBars, eBars, _last.OR5_M, OR5MidColor, DashStyleHelper.Dash, OR5LineWidth, OR5TextSize);
                    } else { RemoveORLevel("OR5_H"); RemoveORLevel("OR5_L"); RemoveORLevel("OR5_M"); }
                    if (show5Ext)
                    {
                        for(int i=0;i<NumExtensions;i++){
                            DrawORLevelHist($"OR5_Ext{i+1}Up",$"OR5 Ext{i+1} Up", sBars, eBars, _last.OR5_ExtUp[i], OR5ExtUpColor, DashStyleHelper.DashDot, OR5LineWidth, OR5TextSize);
                            DrawORLevelHist($"OR5_Ext{i+1}Dn",$"OR5 Ext{i+1} Dn", sBars, eBars, _last.OR5_ExtDn[i], OR5ExtDnColor, DashStyleHelper.DashDot, OR5LineWidth, OR5TextSize);
                        }
                    }
                    for(int i=(show5Ext?NumExtensions:0);i<5;i++){ RemoveORLevel($"OR5_Ext{i+1}Up"); RemoveORLevel($"OR5_Ext{i+1}Dn"); }
                    if (!show5) { for(int i=0;i<5;i++){ RemoveORLevel($"OR5_Ext{i+1}Up"); RemoveORLevel($"OR5_Ext{i+1}Dn"); } }
                }
                else
                {
                    // mode historique N>1 : dessine chaque session avec tag unique par date (evite ecrasement)
                    // on nettoie d'abord les tags de la derniere session seule (pour transition 1->N)
                    RemoveORLevel("OR30_H"); RemoveORLevel("OR30_L"); RemoveORLevel("OR30_M");
                    for(int i=0;i<5;i++){ RemoveORLevel($"OR30_Ext{i+1}Up"); RemoveORLevel($"OR30_Ext{i+1}Dn"); RemoveORLevel($"OR5_Ext{i+1}Up"); RemoveORLevel($"OR5_Ext{i+1}Dn"); }
                    RemoveORLevel("OR5_H"); RemoveORLevel("OR5_L"); RemoveORLevel("OR5_M");
                    for(int s=startIdx; s<sorted.Count; s++)
                    {
                        DateTime od = sorted[s];
                        ORLLevels lvl = _hist[od];
                        if (lvl == null) continue;
                        // overnight : fin = open de la journee suivante (vrai open connu, lundi apres
                        // vendredi) ; sinon fin de session. Affichage uniquement, Values intouches.
                        DateTime eDt;
                        try
                        {
                            DateTime nextOd = (s + 1 < sorted.Count) ? sorted[s + 1] : DateTime.MaxValue;
                            if (ExtendOvernight) eDt = GetEndDtDisplay(od, nextOd);
                            else { eDt = GetEndDt(od); if (nextOd < eDt) eDt = nextOd; }
                            if (s == sorted.Count - 1 && PTime(0) < eDt) eDt = PTime(0);
                        } catch { eDt = GetEndDt(od); }
                        // index cached (1ere bougie >= open) -> demarrage exact 14h30 sur TF intraday
                        // R3 : refind transient uniquement, cache reutilise seulement si valide.
                        int curN = PCur();
                        int sIdx = ValidStartIdx(lvl.OpenBarIndex, od) ? lvl.OpenBarIndex : FindFirstBarAtOrAfter(od);
                        if (sIdx < 0 || sIdx > curN || !ValidStartIdx(sIdx, od)) continue; // debut incoherent : session non dessinee
                        int eIdx = ResolveEndBarIndex(lvl, eDt);
                        if (eIdx < 0) continue; // fin passee irresolvable : jamais d'extension au bout du chart
                        int barsAgoStart = curN - sIdx;
                        int barsAgoEnd = curN - eIdx;
                        if (!CheckDrawInvariant(barsAgoStart, barsAgoEnd, curN, "Nhist")) continue;
                        string suffix = od.ToString("yyyyMMdd");
                        bool sh30 = ShowOR30Level && lvl.HasOR30;
                        bool sh30E = ShowOR30Extensions && lvl.HasOR30;
                        bool sh5 = ShowOR5Level && lvl.HasOR5;
                        bool sh5E = ShowOR5Extensions && lvl.HasOR5;
                        // dessine segment [start -> end] de sa propre session, pas jusqu'a la derniere
                        if (sh30)
                        {
                            DrawORLevelHist($"OR30_H_{suffix}","OR30_H", barsAgoStart, barsAgoEnd, lvl.OR30_H, OR30HighColor, DashStyleHelper.Solid, OR30LineWidth, OR30TextSize);
                            DrawORLevelHist($"OR30_L_{suffix}","OR30_L", barsAgoStart, barsAgoEnd, lvl.OR30_L, OR30LowColor, DashStyleHelper.Solid, OR30LineWidth, OR30TextSize);
                            DrawORLevelHist($"OR30_M_{suffix}","OR30_M", barsAgoStart, barsAgoEnd, lvl.OR30_M, OR30MidColor, DashStyleHelper.Dash, OR30LineWidth, OR30TextSize);
                        } else { RemoveORLevel($"OR30_H_{suffix}"); RemoveORLevel($"OR30_L_{suffix}"); RemoveORLevel($"OR30_M_{suffix}"); }
                        if (sh30 && sh30E)
                        {
                            for(int i=0;i<Math.Min(NumExtensions,5);i++){
                                DrawORLevelHist($"OR30_Ext{i+1}Up_{suffix}",$"OR30 Ext{i+1} Up", barsAgoStart, barsAgoEnd, lvl.OR30_ExtUp[i], OR30ExtUpColor, DashStyleHelper.Dash, OR30LineWidth, OR30TextSize);
                                DrawORLevelHist($"OR30_Ext{i+1}Dn_{suffix}",$"OR30 Ext{i+1} Dn", barsAgoStart, barsAgoEnd, lvl.OR30_ExtDn[i], OR30ExtDnColor, DashStyleHelper.Dash, OR30LineWidth, OR30TextSize);
                            }
                            for(int i=Math.Min(NumExtensions,5);i<5;i++){ RemoveORLevel($"OR30_Ext{i+1}Up_{suffix}"); RemoveORLevel($"OR30_Ext{i+1}Dn_{suffix}"); }
                        } else { for(int i=0;i<5;i++){ RemoveORLevel($"OR30_Ext{i+1}Up_{suffix}"); RemoveORLevel($"OR30_Ext{i+1}Dn_{suffix}"); } }
                        if (sh5)
                        {
                            DrawORLevelHist($"OR5_H_{suffix}","OR5_H", barsAgoStart, barsAgoEnd, lvl.OR5_H, OR5HighColor, DashStyleHelper.Solid, OR5LineWidth, OR5TextSize);
                            DrawORLevelHist($"OR5_L_{suffix}","OR5_L", barsAgoStart, barsAgoEnd, lvl.OR5_L, OR5LowColor, DashStyleHelper.Solid, OR5LineWidth, OR5TextSize);
                            DrawORLevelHist($"OR5_M_{suffix}","OR5_M", barsAgoStart, barsAgoEnd, lvl.OR5_M, OR5MidColor, DashStyleHelper.Dash, OR5LineWidth, OR5TextSize);
                        } else { RemoveORLevel($"OR5_H_{suffix}"); RemoveORLevel($"OR5_L_{suffix}"); RemoveORLevel($"OR5_M_{suffix}"); }
                        if (sh5 && sh5E)
                        {
                            for(int i=0;i<Math.Min(NumExtensions,5);i++){
                                DrawORLevelHist($"OR5_Ext{i+1}Up_{suffix}",$"OR5 Ext{i+1} Up", barsAgoStart, barsAgoEnd, lvl.OR5_ExtUp[i], OR5ExtUpColor, DashStyleHelper.DashDot, OR5LineWidth, OR5TextSize);
                                DrawORLevelHist($"OR5_Ext{i+1}Dn_{suffix}",$"OR5 Ext{i+1} Dn", barsAgoStart, barsAgoEnd, lvl.OR5_ExtDn[i], OR5ExtDnColor, DashStyleHelper.DashDot, OR5LineWidth, OR5TextSize);
                            }
                            for(int i=Math.Min(NumExtensions,5);i<5;i++){ RemoveORLevel($"OR5_Ext{i+1}Up_{suffix}"); RemoveORLevel($"OR5_Ext{i+1}Dn_{suffix}"); }
                        } else { for(int i=0;i<5;i++){ RemoveORLevel($"OR5_Ext{i+1}Up_{suffix}"); RemoveORLevel($"OR5_Ext{i+1}Dn_{suffix}"); } }
                    }
                }
            } catch (Exception ex) { try{Print("ORL "+ex.Message);}catch{} }
        }

        #region Accessors
        [Browsable(false)][XmlIgnore] public double OR30_H=>Values[0][0];
        [Browsable(false)][XmlIgnore] public double OR30_L=>Values[1][0];
        [Browsable(false)][XmlIgnore] public double OR30_M=>Values[2][0];
        [Browsable(false)][XmlIgnore] public double OR30_Ext1Up=>Values[3][0];
        [Browsable(false)][XmlIgnore] public double OR30_Ext2Up=>Values[4][0];
        [Browsable(false)][XmlIgnore] public double OR30_Ext3Up=>Values[5][0];
        [Browsable(false)][XmlIgnore] public double OR30_Ext4Up=>Values[6][0];
        [Browsable(false)][XmlIgnore] public double OR30_Ext5Up=>Values[7][0];
        [Browsable(false)][XmlIgnore] public double OR30_Ext1Dn=>Values[8][0];
        [Browsable(false)][XmlIgnore] public double OR30_Ext2Dn=>Values[9][0];
        [Browsable(false)][XmlIgnore] public double OR30_Ext3Dn=>Values[10][0];
        [Browsable(false)][XmlIgnore] public double OR30_Ext4Dn=>Values[11][0];
        [Browsable(false)][XmlIgnore] public double OR30_Ext5Dn=>Values[12][0];
        [Browsable(false)][XmlIgnore] public double OR5_H=>Values[13][0];
        [Browsable(false)][XmlIgnore] public double OR5_L=>Values[14][0];
        [Browsable(false)][XmlIgnore] public double OR5_M=>Values[15][0];
        [Browsable(false)][XmlIgnore] public double OR5_Ext1Up=>Values[16][0];
        [Browsable(false)][XmlIgnore] public double OR5_Ext2Up=>Values[17][0];
        [Browsable(false)][XmlIgnore] public double OR5_Ext3Up=>Values[18][0];
        [Browsable(false)][XmlIgnore] public double OR5_Ext4Up=>Values[19][0];
        [Browsable(false)][XmlIgnore] public double OR5_Ext5Up=>Values[20][0];
        [Browsable(false)][XmlIgnore] public double OR5_Ext1Dn=>Values[21][0];
        [Browsable(false)][XmlIgnore] public double OR5_Ext2Dn=>Values[22][0];
        [Browsable(false)][XmlIgnore] public double OR5_Ext3Dn=>Values[23][0];
        [Browsable(false)][XmlIgnore] public double OR5_Ext4Dn=>Values[24][0];
        [Browsable(false)][XmlIgnore] public double OR5_Ext5Dn=>Values[25][0];
        [Browsable(false)][XmlIgnore] public double OR30_Range=> double.IsNaN(Values[0][0])||double.IsNaN(Values[1][0])?double.NaN:Values[0][0]-Values[1][0];
        [Browsable(false)][XmlIgnore] public double OR5_Range=> double.IsNaN(Values[13][0])||double.IsNaN(Values[14][0])?double.NaN:Values[13][0]-Values[14][0];
        #endregion

        #region Properties - Session
        [NinjaScriptProperty]
        [Display(Name="Heure d'ouverture", Order=1, GroupName="01 - Session")]
        public TimeSpan OpenTime{get;set;}
        [NinjaScriptProperty]
        [Display(Name="Heure fin journée", Order=2, GroupName="01 - Session")]
        public TimeSpan EndTime{get;set;}
        [NinjaScriptProperty]
        [Display(Name="Ajustement DST US auto", Description = "true = OpenTime/EndTime effectifs +1h en hiver US (EST, nov-mars). Charts supposes a offset fixe (ex: Benin UTC+1).", Order=3, GroupName="01 - Session")]
        public bool AdjustForUsDst{get;set;}
        [NinjaScriptProperty]
        [Display(Name="Etendre overnight", Order=3, GroupName="01 - Session", Description="Si coché, les lignes s'étendent jusqu'au prochain open, sinon s'arrêtent à Heure fin journée")]
        public bool ExtendOvernight{get;set;}
        [NinjaScriptProperty]
        [Display(Name="Nombre d'extensions", Order=4, GroupName="01 - Session")]
        [Range(0,5)] public int NumExtensions{get;set;}
        [NinjaScriptProperty]
        [Display(Name="Sessions historiques à afficher", Order=5, GroupName="01 - Session", Description="1 = dernière uniquement, >1 pour backtesting visuel")]
        [Range(1,100)] public int HistoricalSessions{get;set;}
        #endregion

        #region Properties - OR 30s
        [NinjaScriptProperty]
        [Display(Name="Activer Level 30s", Order=1, GroupName="OR 30s")]
        public bool ShowOR30Level {get;set;}

        [NinjaScriptProperty]
        [Display(Name="Activer Extensions 30s", Order=2, GroupName="OR 30s")]
        public bool ShowOR30Extensions {get;set;}

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Couleur High 30s", Order=3, GroupName="OR 30s")]
        public Brush OR30HighColor {get;set;}

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Couleur Low 30s", Order=4, GroupName="OR 30s")]
        public Brush OR30LowColor {get;set;}

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Couleur Mid 30s", Order=5, GroupName="OR 30s")]
        public Brush OR30MidColor {get;set;}

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Couleur Ext Up 30s", Order=6, GroupName="OR 30s")]
        public Brush OR30ExtUpColor {get;set;}

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Couleur Ext Dn 30s", Order=7, GroupName="OR 30s")]
        public Brush OR30ExtDnColor {get;set;}

        [NinjaScriptProperty]
        [Display(Name="Epaisseur ligne 30s", Order=8, GroupName="OR 30s")]
        [Range(1,5)] public int OR30LineWidth {get;set;}

        [NinjaScriptProperty]
        [Display(Name="Taille texte 30s", Order=9, GroupName="OR 30s")]
        [Range(7,16)] public int OR30TextSize {get;set;}
        #endregion

        #region Properties - OR 5min
        [NinjaScriptProperty]
        [Display(Name="Activer Level 5min", Order=1, GroupName="OR 5min")]
        public bool ShowOR5Level {get;set;}

        [NinjaScriptProperty]
        [Display(Name="Activer Extensions 5min", Order=2, GroupName="OR 5min")]
        public bool ShowOR5Extensions {get;set;}

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Couleur High 5min", Order=3, GroupName="OR 5min")]
        public Brush OR5HighColor {get;set;}

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Couleur Low 5min", Order=4, GroupName="OR 5min")]
        public Brush OR5LowColor {get;set;}

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Couleur Mid 5min", Order=5, GroupName="OR 5min")]
        public Brush OR5MidColor {get;set;}

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Couleur Ext Up 5min", Order=6, GroupName="OR 5min")]
        public Brush OR5ExtUpColor {get;set;}

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Couleur Ext Dn 5min", Order=7, GroupName="OR 5min")]
        public Brush OR5ExtDnColor {get;set;}

        [NinjaScriptProperty]
        [Display(Name="Epaisseur ligne 5min", Order=8, GroupName="OR 5min")]
        [Range(1,5)] public int OR5LineWidth {get;set;}

        [NinjaScriptProperty]
        [Display(Name="Taille texte 5min", Order=9, GroupName="OR 5min")]
        [Range(7,16)] public int OR5TextSize {get;set;}
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MGIOpenRangeLevels[] cacheMGIOpenRangeLevels;
		public MGIOpenRangeLevels MGIOpenRangeLevels(TimeSpan openTime, TimeSpan endTime, bool adjustForUsDst, bool extendOvernight, int numExtensions, int historicalSessions, bool showOR30Level, bool showOR30Extensions, Brush oR30HighColor, Brush oR30LowColor, Brush oR30MidColor, Brush oR30ExtUpColor, Brush oR30ExtDnColor, int oR30LineWidth, int oR30TextSize, bool showOR5Level, bool showOR5Extensions, Brush oR5HighColor, Brush oR5LowColor, Brush oR5MidColor, Brush oR5ExtUpColor, Brush oR5ExtDnColor, int oR5LineWidth, int oR5TextSize)
		{
			return MGIOpenRangeLevels(Input, openTime, endTime, adjustForUsDst, extendOvernight, numExtensions, historicalSessions, showOR30Level, showOR30Extensions, oR30HighColor, oR30LowColor, oR30MidColor, oR30ExtUpColor, oR30ExtDnColor, oR30LineWidth, oR30TextSize, showOR5Level, showOR5Extensions, oR5HighColor, oR5LowColor, oR5MidColor, oR5ExtUpColor, oR5ExtDnColor, oR5LineWidth, oR5TextSize);
		}

		public MGIOpenRangeLevels MGIOpenRangeLevels(ISeries<double> input, TimeSpan openTime, TimeSpan endTime, bool adjustForUsDst, bool extendOvernight, int numExtensions, int historicalSessions, bool showOR30Level, bool showOR30Extensions, Brush oR30HighColor, Brush oR30LowColor, Brush oR30MidColor, Brush oR30ExtUpColor, Brush oR30ExtDnColor, int oR30LineWidth, int oR30TextSize, bool showOR5Level, bool showOR5Extensions, Brush oR5HighColor, Brush oR5LowColor, Brush oR5MidColor, Brush oR5ExtUpColor, Brush oR5ExtDnColor, int oR5LineWidth, int oR5TextSize)
		{
			if (cacheMGIOpenRangeLevels != null)
				for (int idx = 0; idx < cacheMGIOpenRangeLevels.Length; idx++)
					if (cacheMGIOpenRangeLevels[idx] != null && cacheMGIOpenRangeLevels[idx].OpenTime == openTime && cacheMGIOpenRangeLevels[idx].EndTime == endTime && cacheMGIOpenRangeLevels[idx].AdjustForUsDst == adjustForUsDst && cacheMGIOpenRangeLevels[idx].ExtendOvernight == extendOvernight && cacheMGIOpenRangeLevels[idx].NumExtensions == numExtensions && cacheMGIOpenRangeLevels[idx].HistoricalSessions == historicalSessions && cacheMGIOpenRangeLevels[idx].ShowOR30Level == showOR30Level && cacheMGIOpenRangeLevels[idx].ShowOR30Extensions == showOR30Extensions && cacheMGIOpenRangeLevels[idx].OR30HighColor == oR30HighColor && cacheMGIOpenRangeLevels[idx].OR30LowColor == oR30LowColor && cacheMGIOpenRangeLevels[idx].OR30MidColor == oR30MidColor && cacheMGIOpenRangeLevels[idx].OR30ExtUpColor == oR30ExtUpColor && cacheMGIOpenRangeLevels[idx].OR30ExtDnColor == oR30ExtDnColor && cacheMGIOpenRangeLevels[idx].OR30LineWidth == oR30LineWidth && cacheMGIOpenRangeLevels[idx].OR30TextSize == oR30TextSize && cacheMGIOpenRangeLevels[idx].ShowOR5Level == showOR5Level && cacheMGIOpenRangeLevels[idx].ShowOR5Extensions == showOR5Extensions && cacheMGIOpenRangeLevels[idx].OR5HighColor == oR5HighColor && cacheMGIOpenRangeLevels[idx].OR5LowColor == oR5LowColor && cacheMGIOpenRangeLevels[idx].OR5MidColor == oR5MidColor && cacheMGIOpenRangeLevels[idx].OR5ExtUpColor == oR5ExtUpColor && cacheMGIOpenRangeLevels[idx].OR5ExtDnColor == oR5ExtDnColor && cacheMGIOpenRangeLevels[idx].OR5LineWidth == oR5LineWidth && cacheMGIOpenRangeLevels[idx].OR5TextSize == oR5TextSize && cacheMGIOpenRangeLevels[idx].EqualsInput(input))
						return cacheMGIOpenRangeLevels[idx];
			return CacheIndicator<MGIOpenRangeLevels>(new MGIOpenRangeLevels(){ OpenTime = openTime, EndTime = endTime, AdjustForUsDst = adjustForUsDst, ExtendOvernight = extendOvernight, NumExtensions = numExtensions, HistoricalSessions = historicalSessions, ShowOR30Level = showOR30Level, ShowOR30Extensions = showOR30Extensions, OR30HighColor = oR30HighColor, OR30LowColor = oR30LowColor, OR30MidColor = oR30MidColor, OR30ExtUpColor = oR30ExtUpColor, OR30ExtDnColor = oR30ExtDnColor, OR30LineWidth = oR30LineWidth, OR30TextSize = oR30TextSize, ShowOR5Level = showOR5Level, ShowOR5Extensions = showOR5Extensions, OR5HighColor = oR5HighColor, OR5LowColor = oR5LowColor, OR5MidColor = oR5MidColor, OR5ExtUpColor = oR5ExtUpColor, OR5ExtDnColor = oR5ExtDnColor, OR5LineWidth = oR5LineWidth, OR5TextSize = oR5TextSize }, input, ref cacheMGIOpenRangeLevels);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MGIOpenRangeLevels MGIOpenRangeLevels(TimeSpan openTime, TimeSpan endTime, bool adjustForUsDst, bool extendOvernight, int numExtensions, int historicalSessions, bool showOR30Level, bool showOR30Extensions, Brush oR30HighColor, Brush oR30LowColor, Brush oR30MidColor, Brush oR30ExtUpColor, Brush oR30ExtDnColor, int oR30LineWidth, int oR30TextSize, bool showOR5Level, bool showOR5Extensions, Brush oR5HighColor, Brush oR5LowColor, Brush oR5MidColor, Brush oR5ExtUpColor, Brush oR5ExtDnColor, int oR5LineWidth, int oR5TextSize)
		{
			return indicator.MGIOpenRangeLevels(Input, openTime, endTime, adjustForUsDst, extendOvernight, numExtensions, historicalSessions, showOR30Level, showOR30Extensions, oR30HighColor, oR30LowColor, oR30MidColor, oR30ExtUpColor, oR30ExtDnColor, oR30LineWidth, oR30TextSize, showOR5Level, showOR5Extensions, oR5HighColor, oR5LowColor, oR5MidColor, oR5ExtUpColor, oR5ExtDnColor, oR5LineWidth, oR5TextSize);
		}

		public Indicators.MGIOpenRangeLevels MGIOpenRangeLevels(ISeries<double> input , TimeSpan openTime, TimeSpan endTime, bool adjustForUsDst, bool extendOvernight, int numExtensions, int historicalSessions, bool showOR30Level, bool showOR30Extensions, Brush oR30HighColor, Brush oR30LowColor, Brush oR30MidColor, Brush oR30ExtUpColor, Brush oR30ExtDnColor, int oR30LineWidth, int oR30TextSize, bool showOR5Level, bool showOR5Extensions, Brush oR5HighColor, Brush oR5LowColor, Brush oR5MidColor, Brush oR5ExtUpColor, Brush oR5ExtDnColor, int oR5LineWidth, int oR5TextSize)
		{
			return indicator.MGIOpenRangeLevels(input, openTime, endTime, adjustForUsDst, extendOvernight, numExtensions, historicalSessions, showOR30Level, showOR30Extensions, oR30HighColor, oR30LowColor, oR30MidColor, oR30ExtUpColor, oR30ExtDnColor, oR30LineWidth, oR30TextSize, showOR5Level, showOR5Extensions, oR5HighColor, oR5LowColor, oR5MidColor, oR5ExtUpColor, oR5ExtDnColor, oR5LineWidth, oR5TextSize);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MGIOpenRangeLevels MGIOpenRangeLevels(TimeSpan openTime, TimeSpan endTime, bool adjustForUsDst, bool extendOvernight, int numExtensions, int historicalSessions, bool showOR30Level, bool showOR30Extensions, Brush oR30HighColor, Brush oR30LowColor, Brush oR30MidColor, Brush oR30ExtUpColor, Brush oR30ExtDnColor, int oR30LineWidth, int oR30TextSize, bool showOR5Level, bool showOR5Extensions, Brush oR5HighColor, Brush oR5LowColor, Brush oR5MidColor, Brush oR5ExtUpColor, Brush oR5ExtDnColor, int oR5LineWidth, int oR5TextSize)
		{
			return indicator.MGIOpenRangeLevels(Input, openTime, endTime, adjustForUsDst, extendOvernight, numExtensions, historicalSessions, showOR30Level, showOR30Extensions, oR30HighColor, oR30LowColor, oR30MidColor, oR30ExtUpColor, oR30ExtDnColor, oR30LineWidth, oR30TextSize, showOR5Level, showOR5Extensions, oR5HighColor, oR5LowColor, oR5MidColor, oR5ExtUpColor, oR5ExtDnColor, oR5LineWidth, oR5TextSize);
		}

		public Indicators.MGIOpenRangeLevels MGIOpenRangeLevels(ISeries<double> input , TimeSpan openTime, TimeSpan endTime, bool adjustForUsDst, bool extendOvernight, int numExtensions, int historicalSessions, bool showOR30Level, bool showOR30Extensions, Brush oR30HighColor, Brush oR30LowColor, Brush oR30MidColor, Brush oR30ExtUpColor, Brush oR30ExtDnColor, int oR30LineWidth, int oR30TextSize, bool showOR5Level, bool showOR5Extensions, Brush oR5HighColor, Brush oR5LowColor, Brush oR5MidColor, Brush oR5ExtUpColor, Brush oR5ExtDnColor, int oR5LineWidth, int oR5TextSize)
		{
			return indicator.MGIOpenRangeLevels(input, openTime, endTime, adjustForUsDst, extendOvernight, numExtensions, historicalSessions, showOR30Level, showOR30Extensions, oR30HighColor, oR30LowColor, oR30MidColor, oR30ExtUpColor, oR30ExtDnColor, oR30LineWidth, oR30TextSize, showOR5Level, showOR5Extensions, oR5HighColor, oR5LowColor, oR5MidColor, oR5ExtUpColor, oR5ExtDnColor, oR5LineWidth, oR5TextSize);
		}
	}
}

#endregion
