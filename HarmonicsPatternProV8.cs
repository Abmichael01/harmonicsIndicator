#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class ZUPHarmonicsRatioBased : Indicator
    {
        private List<PivotPoint> fourHourPivots;
        private int pivotLookback = 1;
        private DateTime lastDebugTime = DateTime.MinValue;
        private int debugCounter = 0;
        private int lastProcessed4HBar = -1;

        // Store the latest detected pattern
        private HarmonicPattern latestPattern = null;

        // Instrument specific tick size and value information
        private readonly Dictionary<string, InstrumentInfo> instrumentInfo = new Dictionary<string, InstrumentInfo>
        {
            {"6B", new InstrumentInfo { TickSize = 0.0001, TickValue = 6.25 }},
            {"CL", new InstrumentInfo { TickSize = 0.01,   TickValue = 10    }},
            {"ES", new InstrumentInfo { TickSize = 0.25,   TickValue = 12.5  }},
            {"GC", new InstrumentInfo { TickSize = 0.1,    TickValue = 10    }},
            {"YM", new InstrumentInfo { TickSize = 1,      TickValue = 5     }}
        };

        // Pattern definitions with Fibonacci ratio ranges
        private readonly Dictionary<string, PatternRatios> patternDefinitions = new Dictionary<string, PatternRatios>
        {
            ["Gartley"] = new PatternRatios
            {
                Name = "Gartley",
                AB_XA = new RatioRange { Min = 0.613, Max = 0.623 },
                BC_AB = new RatioRange { Min = 0.382, Max = 0.886 },
                CD_BC = new RatioRange { Min = 1.27, Max = 1.618 },
                AD_XA = new RatioRange { Min = 0.781, Max = 0.791 }
            },
            ["Bat"] = new PatternRatios
            {
                Name = "Bat",
                AB_XA = new RatioRange { Min = 0.382, Max = 0.50 },
                BC_AB = new RatioRange { Min = 0.382, Max = 0.886 },
                CD_BC = new RatioRange { Min = 1.618, Max = 2.618 },
                AD_XA = new RatioRange { Min = 0.881, Max = 0.891 }
            },
            ["Butterfly"] = new PatternRatios
            {
                Name = "Butterfly",
                AB_XA = new RatioRange { Min = 0.781, Max = 0.791 },
                BC_AB = new RatioRange { Min = 0.382, Max = 0.886 },
                CD_BC = new RatioRange { Min = 1.618, Max = 2.618 },
                AD_XA = new RatioRange { Min = 1.27, Max = 1.618 }
            },
            ["Crab"] = new PatternRatios
            {
                Name = "Crab",
                AB_XA = new RatioRange { Min = 0.382, Max = 0.618 },
                BC_AB = new RatioRange { Min = 0.382, Max = 0.886 },
                CD_BC = new RatioRange { Min = 2.24, Max = 3.618 },
                AD_XA = new RatioRange { Min = 1.613, Max = 1.623 }
            }
        };

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Professional Harmonic Pattern Detector ";
                Name = "HarmonicPatternPro";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;

                // Settings
                PatternLineColor = Brushes.Blue;
                RectangleColor = Brushes.Gray;
                RectangleOpacity = 30;
                ShowPivotDots = true;
                EnableAlerts = true;
                PriceTolerance = 0.015; // 1.5% tolerance for finding pivots near calculated levels
                MaxPatternBars = 50;
                ShowStatusText = true;
                ShowPatternLabel = true;
                ShowPatterns = true;
                PatternVisibilityDays = 5;
                PivotLookback = 1; // require only one bar on each side to confirm pivots

                fourHourPivots = new List<PivotPoint>();

                Print("=== ZUP HARMONICS RATIO-BASED DETECTION INITIALIZED ===");
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 240); // 4H = 240 minutes
                Print("=== 4H DATA SERIES ADDED ===");
            }
            else if (State == State.DataLoaded)
            {
                Print("=== DATA LOADED - RATIO-BASED DETECTION READY ===");
            }
        }

        protected override void OnBarUpdate()
        {
            debugCounter++;

            // Process 4H data when it updates
            if (BarsInProgress == 1) // 4H data series
            {
                if (CurrentBars[1] == lastProcessed4HBar) return;
                lastProcessed4HBar = CurrentBars[1];

                Print($"[4H UPDATE #{debugCounter}] NEW 4H Bar - Current: {CurrentBars[1]}");

                if (CurrentBars[1] < pivotLookback * 2)
                {
                    Print($"[4H UPDATE] Not enough 4H bars for pivot detection. Need: {pivotLookback * 2}, Have: {CurrentBars[1]}");
                    return;
                }

                // Check for new pivots
                CheckForNewPivots();

                // Clean up old pivots
                CleanupOldPivots();

                // Use RATIO-BASED pattern detection
                if (fourHourPivots.Count >= 4) // Need at least X and A to start calculating
                {
                    Print($"[4H UPDATE] Sufficient pivots ({fourHourPivots.Count}) - Using RATIO-BASED detection...");
                    DetectPatternsUsingRatios();
                }
                else
                {
                    Print($"[4H UPDATE] Building pivot history. Have: {fourHourPivots.Count}, Need: 4+");
                }
            }

            // Display on primary timeframe
            if (BarsInProgress == 0)
            {
                if ((DateTime.Now - lastDebugTime).TotalSeconds > 30)
                {
                    lastDebugTime = DateTime.Now;
                    Print($"[60M UPDATE #{debugCounter}] Primary chart update");

                    if (CurrentBars.Length > 1)
                    {
                        Print($"[STATUS] 4H Bars: {CurrentBars[1]}, Pivots: {fourHourPivots.Count}");
                    }
                }

                // Status display
                if (ShowStatusText)
                {
                    string statusText = "ZUP Ratio-Based 4H Harmonics - ";
                    if (CurrentBars.Length > 1 && CurrentBars[1] > 0)
                    {
                        if (latestPattern != null)
                        {
                            statusText += $"🎯 {latestPattern.Type} PATTERN DETECTED!";
                        }
                        else
                        {
                            statusText += $"SCANNING - Pivots:{fourHourPivots.Count}";
                        }
                    }
                    else
                    {
                        statusText += "LOADING 4H DATA...";
                    }
                    Draw.TextFixed(this, "StatusText", statusText, TextPosition.TopLeft, Brushes.White,
                        new SimpleFont("Arial", 11), Brushes.DarkBlue, Brushes.DarkBlue, 90);
                }
                else
                {
                    RemoveDrawObject("StatusText");
                }

                if (latestPattern != null)
                {
                    bool expired = PatternVisibilityDays > 0 && Time[0] > latestPattern.DetectionTime.AddDays(PatternVisibilityDays);
                    if (expired)
                    {
                        ClearPatternDrawings("RatioPattern", true);
                        latestPattern = null;
                    }
                    else
                    {
                        DrawHarmonicPattern(latestPattern, ShowPatterns, ShowPatternLabel);
                    }
                }
            }
        }

        private double GetInstrumentTickSize()
        {
            string name = Instrument?.MasterInstrument?.Name ?? string.Empty;
            return instrumentInfo.TryGetValue(name, out InstrumentInfo info) ? info.TickSize : TickSize;
        }

        private double GetInstrumentTickValue()
        {
            string name = Instrument?.MasterInstrument?.Name ?? string.Empty;
            return instrumentInfo.TryGetValue(name, out InstrumentInfo info) ? info.TickValue : 1.0;
        }

        // Convert a price movement to currency value based on instrument specifics
        private double PriceToCurrency(double priceChange)
        {
            return priceChange / GetInstrumentTickSize() * GetInstrumentTickValue();
        }

        // Convert currency value back to price movement
        private double CurrencyToPrice(double value)
        {
            return value / GetInstrumentTickValue() * GetInstrumentTickSize();
        }

        private void CheckForNewPivots()
        {
            Print($"[PIVOT CHECK] Checking for new pivots at current 4H bar position...");

            int checkPosition = pivotLookback;

            if (CurrentBars[1] < checkPosition + pivotLookback) return;

            double tickSize = GetInstrumentTickSize();
            double checkHigh = Math.Round(Highs[1][checkPosition] / tickSize) * tickSize;
            double checkLow = Math.Round(Lows[1][checkPosition] / tickSize) * tickSize;
            DateTime checkTime = Times[1][checkPosition];
            int checkBarIndex = CurrentBars[1] - checkPosition;

            bool isPivotHigh = true;
            for (int i = 1; i <= pivotLookback; i++)
            {
                if (Highs[1][checkPosition + i] >= checkHigh || Highs[1][checkPosition - i] >= checkHigh)
                {
                    isPivotHigh = false;
                    break;
                }
            }

            bool isPivotLow = true;
            for (int i = 1; i <= pivotLookback; i++)
            {
                if (Lows[1][checkPosition + i] <= checkLow || Lows[1][checkPosition - i] <= checkLow)
                {
                    isPivotLow = false;
                    break;
                }
            }

            if (isPivotHigh && !IsDuplicatePivot(checkBarIndex, checkHigh, PivotType.High))
            {
                var newPivot = new PivotPoint
                {
                    BarIndex = checkBarIndex,
                    Price = checkHigh,
                    Type = PivotType.High,
                    Time = checkTime
                };

                fourHourPivots.Add(newPivot);
                Print($"[NEW PIVOT] 🔴 HIGH FORMED at {checkTime:MM/dd HH:mm} - Price: {checkHigh:F1} - Bar: {checkBarIndex}");

                if (ShowPivotDots)
                    Draw.Dot(this, "4HPivotHigh_" + checkBarIndex, false, checkTime, checkHigh, Brushes.Red);
            }

            if (isPivotLow && !IsDuplicatePivot(checkBarIndex, checkLow, PivotType.Low))
            {
                var newPivot = new PivotPoint
                {
                    BarIndex = checkBarIndex,
                    Price = checkLow,
                    Type = PivotType.Low,
                    Time = checkTime
                };

                fourHourPivots.Add(newPivot);
                Print($"[NEW PIVOT] 🟢 LOW FORMED at {checkTime:MM/dd HH:mm} - Price: {checkLow:F1} - Bar: {checkBarIndex}");

                if (ShowPivotDots)
                    Draw.Dot(this, "4HPivotLow_" + checkBarIndex, false, checkTime, checkLow, Brushes.Green);
            }
        }

        private bool IsDuplicatePivot(int barIndex, double price, PivotType type)
        {
            foreach (var pivot in fourHourPivots)
            {
                if (pivot.Type == type &&
                     Math.Abs(pivot.BarIndex - barIndex) <= 1 &&
                    Math.Abs(pivot.Price - price) < GetInstrumentTickSize() * 2)
                {
                    return true;
                }
            }
            return false;
        }

        private void CleanupOldPivots()
        {
            if (fourHourPivots.Count > 100)
            {
                var sortedPivots = fourHourPivots.OrderBy(p => p.BarIndex).ToList();
                int toRemove = fourHourPivots.Count - 100;

                for (int i = 0; i < toRemove; i++)
                {
                    fourHourPivots.Remove(sortedPivots[i]);
                }

                Print($"[CLEANUP] Removed {toRemove} old pivots, keeping {fourHourPivots.Count} recent ones");
            }
        }

        // RATIO-BASED DETECTION
        private void DetectPatternsUsingRatios()
        {
            Print($"[RATIO DETECTION] Starting ratio-based pattern detection with {fourHourPivots.Count} pivots");

            var sortedPivots = fourHourPivots.OrderBy(p => p.BarIndex).ToList();
            HarmonicPattern newestPattern = null;

            foreach (var patternDef in patternDefinitions.Values)
            {
                for (int xIndex = 0; xIndex < sortedPivots.Count - 4; xIndex++)
                {
                    var X = sortedPivots[xIndex];
                    for (int aIndex = xIndex + 1; aIndex < sortedPivots.Count - 3; aIndex++)
                    {
                        var A = sortedPivots[aIndex];
                        var pattern = TryFindPattern(X, A, patternDef, sortedPivots, aIndex);
                        if (pattern != null && (newestPattern == null || pattern.D.BarIndex > newestPattern.D.BarIndex))
                            newestPattern = pattern;
                    }
                }
            }

            if (newestPattern != null)
            {
                if (latestPattern == null || newestPattern.D.BarIndex > latestPattern.D.BarIndex)
                {
                    latestPattern = newestPattern;
                    if (EnableAlerts)
                    {
                        Alert("RatioBasedHarmonic", Priority.High,
                              $"🎯 {newestPattern.Type} Pattern Detected!",
                              "", 15, Brushes.Yellow, Brushes.Black);
                    }
                }
            }
        }

        private HarmonicPattern TryFindPattern(PivotPoint X, PivotPoint A, PatternRatios ratios, List<PivotPoint> pivots, int aIndex)
        {
            bool isBullish = X.Type == PivotType.Low && A.Type == PivotType.High;
            bool isBearish = X.Type == PivotType.High && A.Type == PivotType.Low;
            if (!isBullish && !isBearish) return null;
            if (A.BarIndex - X.BarIndex > MaxPatternBars) return null;

            double XA = Math.Abs(A.Price - X.Price);

            for (int bIndex = aIndex + 1; bIndex < pivots.Count - 2; bIndex++)
            {
                var B = pivots[bIndex];
                if (B.Type != X.Type) continue;
                double AB = Math.Abs(B.Price - A.Price);
                double abRatio = AB / XA;
                if (!IsWithinRange(abRatio, ratios.AB_XA)) continue;
                if (isBullish && (B.Price <= X.Price || B.Price >= A.Price)) continue;
                if (isBearish && (B.Price >= X.Price || B.Price <= A.Price)) continue;

                for (int cIndex = bIndex + 1; cIndex < pivots.Count - 1; cIndex++)
                {
                    var C = pivots[cIndex];
                    if (C.Type != A.Type) continue;
                    double BC = Math.Abs(C.Price - B.Price);
                    double bcRatio = BC / AB;
                    if (!IsWithinRange(bcRatio, ratios.BC_AB)) continue;
                    if (isBullish && !(C.Price < A.Price && C.Price > B.Price)) continue;
                    if (isBearish && !(C.Price > A.Price && C.Price < B.Price)) continue;

                    for (int dIndex = cIndex + 1; dIndex < pivots.Count; dIndex++)
                    {
                        var D = pivots[dIndex];
                        if (D.Type != X.Type) continue;
                        double CD = Math.Abs(D.Price - C.Price);
                        double cdRatio = CD / BC;
                        if (!IsWithinRange(cdRatio, ratios.CD_BC)) continue;
                        if (isBullish && !(D.Price < C.Price && D.Price < B.Price)) continue;
                        if (isBearish && !(D.Price > C.Price && D.Price > B.Price)) continue;

                        double AD = Math.Abs(D.Price - X.Price);
                        double adRatio = AD / XA;
                        if (!IsWithinRange(adRatio, ratios.AD_XA)) continue;

                        return new HarmonicPattern
                        {
                            Type = ratios.Name,
                            X = X,
                            A = A,
                            B = B,
                            C = C,
                            D = D,
                            IsBullish = isBullish,
                            DetectionTime = DateTime.Now
                        };
                    }
                }
            }
            return null;
        }

        private bool IsWithinRange(double value, RatioRange range)
        {
            return value >= range.Min && value <= range.Max;
        }

        private void DrawHarmonicPattern(HarmonicPattern pattern, bool drawLines = true, bool showLabel = true)
        {
            if (pattern == null) return;

            string tag = "RatioPattern";
            ClearPatternDrawings(tag);

            if (drawLines)
            {
                Draw.Line(this, tag + "_XA", false, pattern.X.Time, pattern.X.Price, pattern.A.Time, pattern.A.Price, PatternLineColor, DashStyleHelper.Solid, 3);
                Draw.Line(this, tag + "_AB", false, pattern.A.Time, pattern.A.Price, pattern.B.Time, pattern.B.Price, PatternLineColor, DashStyleHelper.Solid, 3);
                Draw.Line(this, tag + "_BC", false, pattern.B.Time, pattern.B.Price, pattern.C.Time, pattern.C.Price, PatternLineColor, DashStyleHelper.Solid, 3);
                Draw.Line(this, tag + "_CD", false, pattern.C.Time, pattern.C.Price, pattern.D.Time, pattern.D.Price, PatternLineColor, DashStyleHelper.Solid, 3);

                Draw.Line(this, tag + "_XB", false, pattern.X.Time, pattern.X.Price, pattern.B.Time, pattern.B.Price, PatternLineColor, DashStyleHelper.Dot, 2);
                Draw.Line(this, tag + "_AC", false, pattern.A.Time, pattern.A.Price, pattern.C.Time, pattern.C.Price, PatternLineColor, DashStyleHelper.Dot, 2);
            }

            DateTime endTime = pattern.D.Time.AddDays(3);
            double tickSize = GetInstrumentTickSize();
            double priceRange = tickSize * 10; // show 10 ticks above and below
            Draw.Rectangle(this, tag + "_Rect", false, pattern.D.Time, pattern.D.Price - priceRange,
                 endTime, pattern.D.Price + priceRange, Brushes.Transparent, RectangleColor, RectangleOpacity);

            if (showLabel)
            {
                string direction = pattern.IsBullish ? "Bullish" : "Bearish";
                double moveValue = PriceToCurrency(Math.Abs(pattern.C.Price - pattern.D.Price));
                Draw.TextFixed(this, tag + "_Label", $"🎯 {pattern.Type} {direction} (${moveValue:F2})",
                     TextPosition.TopRight, Brushes.White, new SimpleFont("Arial", 14),
                     Brushes.DarkGreen, Brushes.DarkGreen, 80);
            }

            if (drawLines)
                Print($"[DRAWING] ✅ {pattern.Type} pattern drawing complete");
        }

        private void ClearPatternDrawings(string tag, bool keepRectangle = false)
        {
            RemoveDrawObject(tag + "_XA");
            RemoveDrawObject(tag + "_AB");
            RemoveDrawObject(tag + "_BC");
            RemoveDrawObject(tag + "_CD");
            RemoveDrawObject(tag + "_XB");
            RemoveDrawObject(tag + "_AC");
            if (!keepRectangle)
                RemoveDrawObject(tag + "_Rect");
            RemoveDrawObject(tag + "_Label");
        }

        #region Properties

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Pattern Line Color", Order = 1, GroupName = "Visual")]
        public Brush PatternLineColor { get; set; }

        [Browsable(false)]
        public string PatternLineColorSerializable
        {
            get { return Serialize.BrushToString(PatternLineColor); }
            set { PatternLineColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Rectangle Color", Order = 2, GroupName = "Visual")]
        public Brush RectangleColor { get; set; }

        [Browsable(false)]
        public string RectangleColorSerializable
        {
            get { return Serialize.BrushToString(RectangleColor); }
            set { RectangleColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Rectangle Opacity", Order = 3, GroupName = "Visual")]
        public int RectangleOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Pivot Dots", Order = 4, GroupName = "Visual")]
        public bool ShowPivotDots { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Status Text", Order = 5, GroupName = "Visual")]
        public bool ShowStatusText { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Pattern Label", Order = 6, GroupName = "Visual")]
        public bool ShowPatternLabel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Patterns", Order = 7, GroupName = "Visual")]
        public bool ShowPatterns { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Alerts", Order = 1, GroupName = "Alerts")]
        public bool EnableAlerts { get; set; }

        [NinjaScriptProperty]
        [Range(0.005, 0.05)]
        [Display(Name = "Price Tolerance", Order = 6, GroupName = "Detection")]
        public double PriceTolerance { get; set; }

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Max Pattern Bars", Order = 7, GroupName = "Detection")]
        public int MaxPatternBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, 30)]
        [Display(Name = "Pattern Visibility Days", Order = 8, GroupName = "Detection")]
        public int PatternVisibilityDays { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5)]
        [Display(Name = "Pivot Confirmation Bars", Order = 9, GroupName = "Detection")]
        public int PivotLookback
        {
            get { return pivotLookback; }
            set { pivotLookback = value; }
        }

        #endregion
    }

    public class InstrumentInfo
    {
        public double TickSize { get; set; }
        public double TickValue { get; set; }
    }

    public class HarmonicPattern
    {
        public string Type { get; set; }
        public PivotPoint X { get; set; }
        public PivotPoint A { get; set; }
        public PivotPoint B { get; set; }
        public PivotPoint C { get; set; }
        public PivotPoint D { get; set; }
        public bool IsBullish { get; set; }
        public DateTime DetectionTime { get; set; }
    }

    public class PatternRatios
    {
        public string Name { get; set; }
        public RatioRange AB_XA { get; set; }
        public RatioRange BC_AB { get; set; }
        public RatioRange CD_BC { get; set; }
        public RatioRange AD_XA { get; set; }
    }

    public class RatioRange
    {
        public double Min { get; set; }
        public double Max { get; set; }
    }

    public class PivotPoint
    {
        public int BarIndex { get; set; }
        public double Price { get; set; }
        public PivotType Type { get; set; }
        public DateTime Time { get; set; }
    }

    public enum PivotType
    {
        High,
        Low
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private ZUPHarmonicsRatioBased[] cacheZUPHarmonicsRatioBased;
        public ZUPHarmonicsRatioBased ZUPHarmonicsRatioBased(Brush patternLineColor, Brush rectangleColor, int rectangleOpacity, bool showPivotDots, bool showStatusText, bool showPatternLabel, bool showPatterns, bool enableAlerts, double priceTolerance, int maxPatternBars, int patternVisibilityDays, int pivotLookback)
        {
            return ZUPHarmonicsRatioBased(Input, patternLineColor, rectangleColor, rectangleOpacity, showPivotDots, showStatusText, showPatternLabel, showPatterns, enableAlerts, priceTolerance, maxPatternBars, patternVisibilityDays, pivotLookback);
        }

        public ZUPHarmonicsRatioBased ZUPHarmonicsRatioBased(ISeries<double> input, Brush patternLineColor, Brush rectangleColor, int rectangleOpacity, bool showPivotDots, bool showStatusText, bool showPatternLabel, bool showPatterns, bool enableAlerts, double priceTolerance, int maxPatternBars, int patternVisibilityDays, int pivotLookback)
        {
            if (cacheZUPHarmonicsRatioBased != null)
                for (int idx = 0; idx < cacheZUPHarmonicsRatioBased.Length; idx++)
                    if (cacheZUPHarmonicsRatioBased[idx] != null && cacheZUPHarmonicsRatioBased[idx].PatternLineColor == patternLineColor && cacheZUPHarmonicsRatioBased[idx].RectangleColor == rectangleColor && cacheZUPHarmonicsRatioBased[idx].RectangleOpacity == rectangleOpacity && cacheZUPHarmonicsRatioBased[idx].ShowPivotDots == showPivotDots && cacheZUPHarmonicsRatioBased[idx].ShowStatusText == showStatusText && cacheZUPHarmonicsRatioBased[idx].ShowPatternLabel == showPatternLabel && cacheZUPHarmonicsRatioBased[idx].ShowPatterns == showPatterns && cacheZUPHarmonicsRatioBased[idx].EnableAlerts == enableAlerts && cacheZUPHarmonicsRatioBased[idx].PriceTolerance == priceTolerance && cacheZUPHarmonicsRatioBased[idx].MaxPatternBars == maxPatternBars && cacheZUPHarmonicsRatioBased[idx].PatternVisibilityDays == patternVisibilityDays && cacheZUPHarmonicsRatioBased[idx].PivotLookback == pivotLookback && cacheZUPHarmonicsRatioBased[idx].EqualsInput(input))
                        return cacheZUPHarmonicsRatioBased[idx];
            return CacheIndicator<ZUPHarmonicsRatioBased>(new ZUPHarmonicsRatioBased() { PatternLineColor = patternLineColor, RectangleColor = rectangleColor, RectangleOpacity = rectangleOpacity, ShowPivotDots = showPivotDots, ShowStatusText = showStatusText, ShowPatternLabel = showPatternLabel, ShowPatterns = showPatterns, EnableAlerts = enableAlerts, PriceTolerance = priceTolerance, MaxPatternBars = maxPatternBars, PatternVisibilityDays = patternVisibilityDays, PivotLookback = pivotLookback }, input, ref cacheZUPHarmonicsRatioBased);
        }
    }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
    {
        public Indicators.ZUPHarmonicsRatioBased ZUPHarmonicsRatioBased(Brush patternLineColor, Brush rectangleColor, int rectangleOpacity, bool showPivotDots, bool showStatusText, bool showPatternLabel, bool showPatterns, bool enableAlerts, double priceTolerance, int maxPatternBars, int patternVisibilityDays, int pivotLookback)
        {
            return indicator.ZUPHarmonicsRatioBased(Input, patternLineColor, rectangleColor, rectangleOpacity, showPivotDots, showStatusText, showPatternLabel, showPatterns, enableAlerts, priceTolerance, maxPatternBars, patternVisibilityDays, pivotLookback);
        }

        public Indicators.ZUPHarmonicsRatioBased ZUPHarmonicsRatioBased(ISeries<double> input, Brush patternLineColor, Brush rectangleColor, int rectangleOpacity, bool showPivotDots, bool showStatusText, bool showPatternLabel, bool showPatterns, bool enableAlerts, double priceTolerance, int maxPatternBars, int patternVisibilityDays, int pivotLookback)
        {
            return indicator.ZUPHarmonicsRatioBased(input, patternLineColor, rectangleColor, rectangleOpacity, showPivotDots, showStatusText, showPatternLabel, showPatterns, enableAlerts, priceTolerance, maxPatternBars, patternVisibilityDays, pivotLookback);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.ZUPHarmonicsRatioBased ZUPHarmonicsRatioBased(Brush patternLineColor, Brush rectangleColor, int rectangleOpacity, bool showPivotDots, bool showStatusText, bool showPatternLabel, bool showPatterns, bool enableAlerts, double priceTolerance, int maxPatternBars, int patternVisibilityDays, int pivotLookback)
        {
            return indicator.ZUPHarmonicsRatioBased(Input, patternLineColor, rectangleColor, rectangleOpacity, showPivotDots, showStatusText, showPatternLabel, showPatterns, enableAlerts, priceTolerance, maxPatternBars, patternVisibilityDays, pivotLookback);
        }

        public Indicators.ZUPHarmonicsRatioBased ZUPHarmonicsRatioBased(ISeries<double> input, Brush patternLineColor, Brush rectangleColor, int rectangleOpacity, bool showPivotDots, bool showStatusText, bool showPatternLabel, bool showPatterns, bool enableAlerts, double priceTolerance, int maxPatternBars, int patternVisibilityDays, int pivotLookback)
        {
            return indicator.ZUPHarmonicsRatioBased(input, patternLineColor, rectangleColor, rectangleOpacity, showPivotDots, showStatusText, showPatternLabel, showPatterns, enableAlerts, priceTolerance, maxPatternBars, patternVisibilityDays, pivotLookback);
        }
    }
}

#endregion

