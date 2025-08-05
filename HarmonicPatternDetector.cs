#region Using declarations
using System;
using System.Collections.Generic;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class HarmonicPatternDetector : Indicator
    {
        private Swing swing;
        private List<Tuple<int, double>> swingPoints = new List<Tuple<int, double>>();

        [Range(2, 20), NinjaScriptProperty]
        [Display(Name = "Swing Strength", Order = 1, GroupName = "Parameters")]
        public int SwingStrength { get; set; } = 5;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Detects Bullish Gartley harmonic patterns using the built-in Swing indicator";
                Name = "HarmonicPatternDetector";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
            }
            else if (State == State.DataLoaded)
            {
                swing = Swing(Close, SwingStrength);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 20) return;

            // Check for new swing high
            if (swing.SwingHigh[0] != 0)
            {
                int barIdx = CurrentBar - swing.SwingHighBar(0, 1, CurrentBar);
                double price = swing.SwingHigh[0];
                if (swingPoints.Count == 0 || swingPoints[swingPoints.Count - 1].Item1 != barIdx)
                    swingPoints.Add(new Tuple<int, double>(barIdx, price));
            }
            // Check for new swing low
            if (swing.SwingLow[0] != 0)
            {
                int barIdx = CurrentBar - swing.SwingLowBar(0, 1, CurrentBar);
                double price = swing.SwingLow[0];
                if (swingPoints.Count == 0 || swingPoints[swingPoints.Count - 1].Item1 != barIdx)
                    swingPoints.Add(new Tuple<int, double>(barIdx, price));
            }

            // Keep only the last 5 swing points
            while (swingPoints.Count > 5)
                swingPoints.RemoveAt(0);

            // Try to detect a Bullish Gartley pattern
            if (swingPoints.Count == 5)
            {
                var X = swingPoints[0];
                var A = swingPoints[1];
                var B = swingPoints[2];
                var C = swingPoints[3];
                var D = swingPoints[4];

                // Check for Bullish pattern (X > A > B < C > D)
                if (X.Item2 > A.Item2 && A.Item2 > B.Item2 && B.Item2 < C.Item2 && C.Item2 > D.Item2)
                {
                    double AB = Math.Abs(B.Item2 - A.Item2);
                    double XA = Math.Abs(A.Item2 - X.Item2);
                    double BC = Math.Abs(C.Item2 - B.Item2);
                    double CD = Math.Abs(D.Item2 - C.Item2);

                    double AB_XA = AB / XA;
                    double BC_AB = BC / AB;
                    double CD_BC = CD / BC;

                    // Gartley pattern ratios (with some tolerance)
                    if (AB_XA > 0.518 && AB_XA < 0.718 &&
                        BC_AB > 0.282 && BC_AB < 0.986 &&
                        CD_BC > 1.07 && CD_BC < 1.818)
                    {
                        // Draw the pattern with a unique tag
                        string tag = "Gartley" + CurrentBar + "-" + Guid.NewGuid();
                        Draw.Polygon(this, tag, false,
                            new[] {
                                new ChartAnchor { BarIndex = X.Item1, Price = X.Item2 },
                                new ChartAnchor { BarIndex = A.Item1, Price = A.Item2 },
                                new ChartAnchor { BarIndex = B.Item1, Price = B.Item2 },
                                new ChartAnchor { BarIndex = C.Item1, Price = C.Item2 },
                                new ChartAnchor { BarIndex = D.Item1, Price = D.Item2 }
                            },
                            Brushes.CornflowerBlue, Brushes.CornflowerBlue, 2);

                        Print("Bullish Gartley detected at bar: " + CurrentBar);
                    }
                }
            }
        }
    }
}
