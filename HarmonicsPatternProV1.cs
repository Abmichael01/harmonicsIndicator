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
        private int pivotLookback = 3;
        private DateTime lastDebugTime = DateTime.MinValue;
        private int debugCounter = 0;
        private int lastProcessed4HBar = -1;
        
        // Store the latest detected pattern
        private HarmonicPattern latestPattern = null;
        
        // Pattern definitions with exact ratios
        private readonly Dictionary<string, PatternRatios> patternDefinitions = new Dictionary<string, PatternRatios>
        {
            ["Gartley"] = new PatternRatios 
            { 
                AB_XA = 0.618, 
                BC_AB = 0.382, 
                CD_BC = 1.272, 
                XD_XA = 0.786,
                Name = "Gartley"
            },
            ["Bat"] = new PatternRatios 
            { 
                AB_XA = 0.382, 
                BC_AB = 0.382, 
                CD_BC = 1.618, 
                XD_XA = 0.886,
                Name = "Bat"
            },
            ["Butterfly"] = new PatternRatios 
            { 
                AB_XA = 0.786, 
                BC_AB = 0.382, 
                CD_BC = 1.618, 
                XD_XA = 1.27,
                Name = "Butterfly"
            },
            ["Crab"] = new PatternRatios 
            { 
                AB_XA = 0.382, 
                BC_AB = 0.382, 
                CD_BC = 2.618, 
                XD_XA = 1.618,
                Name = "Crab"
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
                
                // Draw the latest pattern if it exists
                if (latestPattern != null)
                {
                    DrawHarmonicPattern(latestPattern);
                }
            }
        }

        private void CheckForNewPivots()
        {
            Print($"[PIVOT CHECK] Checking for new pivots at current 4H bar position...");
            
            int checkPosition = pivotLookback;
            
            if (CurrentBars[1] < checkPosition + pivotLookback) return;
            
            double checkHigh = Highs[1][checkPosition];
            double checkLow = Lows[1][checkPosition];
            DateTime checkTime = Times[1][checkPosition];
            int checkBarIndex = CurrentBars[1] - checkPosition;
            
            // Check for PIVOT HIGH
            bool isPivotHigh = true;
            for (int i = 1; i <= pivotLookback; i++)
            {
                if (Highs[1][checkPosition + i] >= checkHigh || Highs[1][checkPosition - i] >= checkHigh)
                {
                    isPivotHigh = false;
                    break;
                }
            }
            
            // Check for PIVOT LOW
            bool isPivotLow = true;
            for (int i = 1; i <= pivotLookback; i++)
            {
                if (Lows[1][checkPosition + i] <= checkLow || Lows[1][checkPosition - i] <= checkLow)
                {
                    isPivotLow = false;
                    break;
                }
            }
            
            // Add new HIGH pivot
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
            
            // Add new LOW pivot
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
                    Math.Abs(pivot.Price - price) < price * 0.001)
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

        // RATIO-BASED DETECTION - The new approach!
        private void DetectPatternsUsingRatios()
        {
            Print($"[RATIO DETECTION] Starting ratio-based pattern detection with {fourHourPivots.Count} pivots");
            
            var sortedPivots = fourHourPivots.OrderBy(p => p.BarIndex).ToList();
            
            // Try each pivot as potential X point
            for (int xIndex = 0; xIndex < sortedPivots.Count - 3; xIndex++)
            {
                var X = sortedPivots[xIndex];
                
                // Find potential A points (must be opposite type to X and after X)
                for (int aIndex = xIndex + 1; aIndex < sortedPivots.Count - 2; aIndex++)
                {
                    var A = sortedPivots[aIndex];
                    
                    // A must be opposite type to X
                    if (A.Type == X.Type) continue;
                    
                    // Check if pattern would be too long
                    if (A.BarIndex - X.BarIndex > MaxPatternBars) continue;
                    
                    Print($"[RATIO DETECTION] Testing X={X.Price:F1}({X.Type}) A={A.Price:F1}({A.Type})");
                    
                    // Try each pattern type
                    foreach (var patternDef in patternDefinitions.Values)
                    {
                        var detectedPattern = TryBuildPatternFromXA(X, A, patternDef, sortedPivots, aIndex);
                        if (detectedPattern != null)
                        {
                            Print($"[RATIO DETECTION] *** {patternDef.Name} PATTERN DETECTED! ***");
                            
                            // Only update if this is a newer pattern
                            if (latestPattern == null || detectedPattern.D.BarIndex > latestPattern.D.BarIndex)
                            {
                                latestPattern = detectedPattern;
                                
                                if (EnableAlerts)
                                {
                                    Alert("RatioBasedHarmonic", Priority.High,
                                          $"🎯 {patternDef.Name} Pattern Detected!",
                                          "", 15, Brushes.Yellow, Brushes.Black);
                                }
                            }
                            
                            return; // Found a pattern, stop searching for now
                        }
                    }
                }
            }
        }

        private HarmonicPattern TryBuildPatternFromXA(PivotPoint X, PivotPoint A, PatternRatios ratios, 
                                                     List<PivotPoint> allPivots, int aIndex)
        {
            Print($"[BUILD PATTERN] Trying to build {ratios.Name} from X={X.Price:F1} A={A.Price:F1}");
            
            // Calculate XA distance
            double XA = Math.Abs(A.Price - X.Price);
            bool isBullish = A.Price > X.Price;
            
            // STEP 1: Calculate where B should be based on AB/XA ratio
            double expectedAB = XA * ratios.AB_XA;
            double expectedBPrice = isBullish ? A.Price - expectedAB : A.Price + expectedAB;
            
            Print($"[BUILD PATTERN] Expected B at {expectedBPrice:F1} (AB={expectedAB:F1}, ratio={ratios.AB_XA})");
            
            // Find actual B pivot near expected price
            var B = FindPivotNearPrice(expectedBPrice, A.Type == PivotType.High ? PivotType.Low : PivotType.High, 
                                      A.BarIndex, allPivots);
            if (B == null)
            {
                Print($"[BUILD PATTERN] No B pivot found near {expectedBPrice:F1}");
                return null;
            }
            
            Print($"[BUILD PATTERN] Found B at {B.Price:F1} (expected {expectedBPrice:F1})");
            
            // STEP 2: Calculate where C should be based on BC/AB ratio
            double actualAB = Math.Abs(B.Price - A.Price);
            double expectedBC = actualAB * ratios.BC_AB;
            double expectedCPrice = isBullish ? B.Price + expectedBC : B.Price - expectedBC;
            
            Print($"[BUILD PATTERN] Expected C at {expectedCPrice:F1} (BC={expectedBC:F1}, ratio={ratios.BC_AB})");
            
            // Find actual C pivot near expected price
            var C = FindPivotNearPrice(expectedCPrice, B.Type == PivotType.High ? PivotType.Low : PivotType.High, 
                                      B.BarIndex, allPivots);
            if (C == null)
            {
                Print($"[BUILD PATTERN] No C pivot found near {expectedCPrice:F1}");
                return null;
            }
            
            Print($"[BUILD PATTERN] Found C at {C.Price:F1} (expected {expectedCPrice:F1})");
            
            // STEP 3: Calculate where D should be based on CD/BC ratio
            double actualBC = Math.Abs(C.Price - B.Price);
            double expectedCD = actualBC * ratios.CD_BC;
            double expectedDPrice = isBullish ? C.Price - expectedCD : C.Price + expectedCD;
            
            Print($"[BUILD PATTERN] Expected D at {expectedDPrice:F1} (CD={expectedCD:F1}, ratio={ratios.CD_BC})");
            
            // Find actual D pivot near expected price
            var D = FindPivotNearPrice(expectedDPrice, C.Type == PivotType.High ? PivotType.Low : PivotType.High, 
                                      C.BarIndex, allPivots);
            if (D == null)
            {
                Print($"[BUILD PATTERN] No D pivot found near {expectedDPrice:F1}");
                return null;
            }
            
            Print($"[BUILD PATTERN] Found D at {D.Price:F1} (expected {expectedDPrice:F1})");
            
            // STEP 4: Verify XD/XA ratio matches pattern requirements
            double actualXD = Math.Abs(D.Price - X.Price);
            double actualXD_XA = actualXD / XA;
            
            Print($"[BUILD PATTERN] Verifying XD/XA ratio: actual={actualXD_XA:F3}, expected={ratios.XD_XA:F3}");
            
            if (IsWithinTolerance(actualXD_XA, ratios.XD_XA, 0.05)) // 5% tolerance for final ratio
            {
                Print($"[BUILD PATTERN] ✅ {ratios.Name} PATTERN CONFIRMED!");
                Print($"[BUILD PATTERN] X:{X.Price:F1} A:{A.Price:F1} B:{B.Price:F1} C:{C.Price:F1} D:{D.Price:F1}");
                
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
            else
            {
                Print($"[BUILD PATTERN] ❌ XD/XA ratio doesn't match {ratios.Name} requirements");
                return null;
            }
        }

        private PivotPoint FindPivotNearPrice(double targetPrice, PivotType expectedType, int afterBarIndex, 
                                            List<PivotPoint> allPivots)
        {
            PivotPoint bestMatch = null;
            double bestDistance = double.MaxValue;
            
            foreach (var pivot in allPivots)
            {
                // Must be correct type and after the reference bar
                if (pivot.Type != expectedType || pivot.BarIndex <= afterBarIndex) continue;
                
                double distance = Math.Abs(pivot.Price - targetPrice);
                double percentDiff = distance / targetPrice;
                
                // Must be within tolerance and closer than previous matches
                if (percentDiff <= PriceTolerance && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestMatch = pivot;
                }
            }
            
            if (bestMatch != null)
            {
                double percentDiff = (bestDistance / targetPrice) * 100;
                Print($"[FIND PIVOT] Found {expectedType} pivot at {bestMatch.Price:F1} within {percentDiff:F2}% of target {targetPrice:F1}");
            }
            
            return bestMatch;
        }

        private bool IsWithinTolerance(double actual, double expected, double tolerance)
        {
            double diff = Math.Abs(actual - expected) / expected;
            return diff <= tolerance;
        }

        private void DrawHarmonicPattern(HarmonicPattern pattern)
        {
            if (pattern == null) return;
            
            string tag = "RatioPattern";
            
            Print($"[DRAWING] Drawing {pattern.Type} pattern");
            
            // Clear previous pattern
            ClearPatternDrawings(tag);
            
            // Draw pattern lines
            Draw.Line(this, tag + "_XA", false, pattern.X.Time, pattern.X.Price, pattern.A.Time, pattern.A.Price, PatternLineColor, DashStyleHelper.Solid, 3);
            Draw.Line(this, tag + "_AB", false, pattern.A.Time, pattern.A.Price, pattern.B.Time, pattern.B.Price, PatternLineColor, DashStyleHelper.Solid, 3);
            Draw.Line(this, tag + "_BC", false, pattern.B.Time, pattern.B.Price, pattern.C.Time, pattern.C.Price, PatternLineColor, DashStyleHelper.Solid, 3);
            Draw.Line(this, tag + "_CD", false, pattern.C.Time, pattern.C.Price, pattern.D.Time, pattern.D.Price, PatternLineColor, DashStyleHelper.Solid, 3);
            
            // Draw internal structure
            Draw.Line(this, tag + "_XB", false, pattern.X.Time, pattern.X.Price, pattern.B.Time, pattern.B.Price, PatternLineColor, DashStyleHelper.Dot, 2);
            Draw.Line(this, tag + "_AC", false, pattern.A.Time, pattern.A.Price, pattern.C.Time, pattern.C.Price, PatternLineColor, DashStyleHelper.Dot, 2);
            
            // Draw rectangle at D
            DateTime endTime = pattern.D.Time.AddDays(3);
            double priceRange = Math.Abs(pattern.C.Price - pattern.D.Price) * 0.5;
            Draw.Rectangle(this, tag + "_Rect", false, pattern.D.Time, pattern.D.Price - priceRange,
                 endTime, pattern.D.Price + priceRange, Brushes.Transparent, RectangleColor, RectangleOpacity);
            
            // Pattern label
            string direction = pattern.IsBullish ? "Bullish" : "Bearish";
            Draw.TextFixed(this, tag + "_Label", $"🎯 {pattern.Type} {direction}",
                 TextPosition.TopRight, Brushes.White, new SimpleFont("Arial", 14),
                 Brushes.DarkGreen, Brushes.DarkGreen, 80);
            
            Print($"[DRAWING] ✅ {pattern.Type} pattern drawing complete");
        }

        private void ClearPatternDrawings(string tag)
        {
            RemoveDrawObject(tag + "_XA");
            RemoveDrawObject(tag + "_AB");
            RemoveDrawObject(tag + "_BC");
            RemoveDrawObject(tag + "_CD");
            RemoveDrawObject(tag + "_XB");
            RemoveDrawObject(tag + "_AC");
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
        [Display(Name = "Enable Alerts", Order = 5, GroupName = "Alerts")]
        public bool EnableAlerts { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.005, 0.05)]
        [Display(Name = "Price Tolerance", Order = 6, GroupName = "Detection")]
        public double PriceTolerance { get; set; }
        
        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Max Pattern Bars", Order = 7, GroupName = "Detection")]
        public int MaxPatternBars { get; set; }
        
        #endregion
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
        public double AB_XA { get; set; }
        public double BC_AB { get; set; }
        public double CD_BC { get; set; }
        public double XD_XA { get; set; }
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
		public ZUPHarmonicsRatioBased ZUPHarmonicsRatioBased(Brush patternLineColor, Brush rectangleColor, int rectangleOpacity, bool showPivotDots, bool enableAlerts, double priceTolerance, int maxPatternBars)
		{
			return ZUPHarmonicsRatioBased(Input, patternLineColor, rectangleColor, rectangleOpacity, showPivotDots, enableAlerts, priceTolerance, maxPatternBars);
		}

		public ZUPHarmonicsRatioBased ZUPHarmonicsRatioBased(ISeries<double> input, Brush patternLineColor, Brush rectangleColor, int rectangleOpacity, bool showPivotDots, bool enableAlerts, double priceTolerance, int maxPatternBars)
		{
			if (cacheZUPHarmonicsRatioBased != null)
				for (int idx = 0; idx < cacheZUPHarmonicsRatioBased.Length; idx++)
					if (cacheZUPHarmonicsRatioBased[idx] != null && cacheZUPHarmonicsRatioBased[idx].PatternLineColor == patternLineColor && cacheZUPHarmonicsRatioBased[idx].RectangleColor == rectangleColor && cacheZUPHarmonicsRatioBased[idx].RectangleOpacity == rectangleOpacity && cacheZUPHarmonicsRatioBased[idx].ShowPivotDots == showPivotDots && cacheZUPHarmonicsRatioBased[idx].EnableAlerts == enableAlerts && cacheZUPHarmonicsRatioBased[idx].PriceTolerance == priceTolerance && cacheZUPHarmonicsRatioBased[idx].MaxPatternBars == maxPatternBars && cacheZUPHarmonicsRatioBased[idx].EqualsInput(input))
						return cacheZUPHarmonicsRatioBased[idx];
			return CacheIndicator<ZUPHarmonicsRatioBased>(new ZUPHarmonicsRatioBased(){ PatternLineColor = patternLineColor, RectangleColor = rectangleColor, RectangleOpacity = rectangleOpacity, ShowPivotDots = showPivotDots, EnableAlerts = enableAlerts, PriceTolerance = priceTolerance, MaxPatternBars = maxPatternBars }, input, ref cacheZUPHarmonicsRatioBased);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ZUPHarmonicsRatioBased ZUPHarmonicsRatioBased(Brush patternLineColor, Brush rectangleColor, int rectangleOpacity, bool showPivotDots, bool enableAlerts, double priceTolerance, int maxPatternBars)
		{
			return indicator.ZUPHarmonicsRatioBased(Input, patternLineColor, rectangleColor, rectangleOpacity, showPivotDots, enableAlerts, priceTolerance, maxPatternBars);
		}

		public Indicators.ZUPHarmonicsRatioBased ZUPHarmonicsRatioBased(ISeries<double> input , Brush patternLineColor, Brush rectangleColor, int rectangleOpacity, bool showPivotDots, bool enableAlerts, double priceTolerance, int maxPatternBars)
		{
			return indicator.ZUPHarmonicsRatioBased(input, patternLineColor, rectangleColor, rectangleOpacity, showPivotDots, enableAlerts, priceTolerance, maxPatternBars);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ZUPHarmonicsRatioBased ZUPHarmonicsRatioBased(Brush patternLineColor, Brush rectangleColor, int rectangleOpacity, bool showPivotDots, bool enableAlerts, double priceTolerance, int maxPatternBars)
		{
			return indicator.ZUPHarmonicsRatioBased(Input, patternLineColor, rectangleColor, rectangleOpacity, showPivotDots, enableAlerts, priceTolerance, maxPatternBars);
		}

		public Indicators.ZUPHarmonicsRatioBased ZUPHarmonicsRatioBased(ISeries<double> input , Brush patternLineColor, Brush rectangleColor, int rectangleOpacity, bool showPivotDots, bool enableAlerts, double priceTolerance, int maxPatternBars)
		{
			return indicator.ZUPHarmonicsRatioBased(input, patternLineColor, rectangleColor, rectangleOpacity, showPivotDots, enableAlerts, priceTolerance, maxPatternBars);
		}
	}
}

#endregion
