using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;

namespace NinjaTrader.NinjaScript.Indicators
{
    // Color map: bright green = strong buy aggression, bright red = strong sell aggression, orange = moderate imbalance,
    // gray = absorption/ balanced flow, yellow outline = extreme pressure overlay.
    public class ScalpingAgresivoLiquidity : Indicator
    {
        private readonly List<LiquidityZone> liquidityZones = new List<LiquidityZone>();
        private LiquidityZone selectedZone;

        [NinjaScriptProperty]
        [Display(Name = "Enable Color Layers", Order = 0, GroupName = "Display")]
        public bool EnableColorLayers { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Strong Buy Brush", Order = 10, GroupName = "Colors")]
        public Brush StrongBuyBrush { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Strong Sell Brush", Order = 11, GroupName = "Colors")]
        public Brush StrongSellBrush { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Moderate Brush", Order = 12, GroupName = "Colors")]
        public Brush ModerateBrush { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Absorption Brush", Order = 13, GroupName = "Colors")]
        public Brush AbsorptionBrush { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Extreme Highlight Brush", Order = 14, GroupName = "Colors")]
        public Brush ExtremeHighlightBrush { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mini Contract Preset", Order = 20, GroupName = "Presets")]
        public bool UseMiniPreset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Micro Contract Preset", Order = 21, GroupName = "Presets")]
        public bool UseMicroPreset { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Moderate Threshold", Order = 30, GroupName = "Thresholds")]
        public double ModerateThreshold { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Strong Threshold", Order = 31, GroupName = "Thresholds")]
        public double StrongThreshold { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Extreme Threshold", Order = 32, GroupName = "Thresholds")]
        public double ExtremeThreshold { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Zone Lifespan (bars)", Order = 40, GroupName = "Zones")]
        public int ZoneLifespan { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Acceptance Tolerance (ticks)", Order = 41, GroupName = "Zones")]
        public double AcceptanceTolerance { get; set; }

        [Range(1, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Zone Thickness (ticks)", Order = 42, GroupName = "Zones")]
        public int ZoneThickness { get; set; }

        [Range(0.0, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "Minimum Volume", Order = 50, GroupName = "Filters")]
        public double MinimumVolume { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Scalping Agresivo Liquidity";
                Description = "Detects aggressive liquidity and highlights imbalances with color-coded zones.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                EnableColorLayers = true;
                StrongBuyBrush = Brushes.LimeGreen;
                StrongSellBrush = Brushes.Red;
                ModerateBrush = Brushes.Orange;
                AbsorptionBrush = Brushes.DimGray;
                ExtremeHighlightBrush = Brushes.Yellow;
                UseMiniPreset = true;
                UseMicroPreset = false;
                ModerateThreshold = 0.15;
                StrongThreshold = 0.30;
                ExtremeThreshold = 0.45;
                ZoneLifespan = 300;
                AcceptanceTolerance = 2;
                ZoneThickness = 4;
                MinimumVolume = 500;
            }
            else if (State == State.Configure)
            {
                liquidityZones.Clear();
                selectedZone = null;
            }
            else if (State == State.DataLoaded)
            {
                ApplyContractPreset();
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
            {
                RenderZones();
                return;
            }

            double bidVolume = ResolveBidVolumeValue();
            double askVolume = ResolveAskVolumeValue();
            double totalVolume = bidVolume + askVolume;
            if (totalVolume < MinimumVolume || totalVolume <= 0)
            {
                CleanupZones();
                RenderZones();
                return;
            }

            double delta = askVolume - bidVolume;
            double imbalanceRatio = totalVolume > 0 ? delta / totalVolume : 0;

            LiquidityZoneColorProfile profile = DetermineColorProfile(imbalanceRatio, delta);
            if (profile.ZoneBrush != null)
            {
                LiquidityZone zone = CreateZone(profile, bidVolume, askVolume, delta);
                liquidityZones.Add(zone);
            }

            CleanupZones();
            RenderZones();
        }

        private LiquidityZoneColorProfile DetermineColorProfile(double imbalanceRatio, double delta)
        {
            LiquidityZoneColorProfile profile = new LiquidityZoneColorProfile();

            if (Math.Abs(imbalanceRatio) < ModerateThreshold)
            {
                // Neutral absorption zones lean gray to signal balance in flow.
                profile.ZoneBrush = EnableColorLayers ? AbsorptionBrush : null;
                profile.Label = "Absorption";
                profile.Side = imbalanceRatio >= 0 ? "Buy" : "Sell";
                profile.HighlightBrush = null;
                return profile;
            }

            bool isBuy = imbalanceRatio > 0;
            double magnitude = Math.Abs(imbalanceRatio);
            Brush baseBrush;

            if (magnitude >= ExtremeThreshold)
            {
                // Extremes get yellow outlines so the trader sees critical aggression instantly.
                baseBrush = isBuy ? StrongBuyBrush : StrongSellBrush;
                profile.HighlightBrush = ExtremeHighlightBrush;
                profile.Label = "Extreme";
            }
            else if (magnitude >= StrongThreshold)
            {
                baseBrush = isBuy ? StrongBuyBrush : StrongSellBrush;
                profile.HighlightBrush = null;
                profile.Label = "Strong";
            }
            else
            {
                baseBrush = ModerateBrush;
                profile.HighlightBrush = null;
                profile.Label = "Moderate";
            }

            profile.ZoneBrush = EnableColorLayers ? baseBrush : AbsorptionBrush;
            profile.Side = isBuy ? "Buy" : "Sell";
            profile.Delta = delta;
            profile.ImbalanceRatio = imbalanceRatio;
            return profile;
        }

        private LiquidityZone CreateZone(LiquidityZoneColorProfile profile, double bidVolume, double askVolume, double delta)
        {
            double price = Close[0];
            double halfTicks = ZoneThickness / 2.0;
            double upper = Instrument.MasterInstrument.RoundToTickSize(price + halfTicks * TickSize);
            double lower = Instrument.MasterInstrument.RoundToTickSize(price - halfTicks * TickSize);

            LiquidityZone zone = new LiquidityZone
            {
                StartBar = CurrentBar,
                PriceCenter = price,
                UpperPrice = Math.Max(upper, lower),
                LowerPrice = Math.Min(upper, lower),
                BidVolume = bidVolume,
                AskVolume = askVolume,
                Delta = delta,
                Side = profile.Side,
                ZoneBrush = profile.ZoneBrush,
                OutlineBrush = profile.HighlightBrush,
                Label = profile.Label
            };

            return zone;
        }

        private void RenderZones()
        {
            if (liquidityZones.Count == 0)
                return;

            foreach (LiquidityZone zone in liquidityZones)
            {
                string baseTag = string.Format(CultureInfo.InvariantCulture, "SAL_ZONE_{0}", zone.StartBar);
                string rectTag = baseTag + "_RECT";
                string textTag = baseTag + "_TEXT";

                int startBarsAgo = Math.Min(CurrentBar - zone.StartBar, ZoneLifespan);
                int endBarsAgo = 0;
                bool isSelected = selectedZone == zone;

                Brush fillBrush = CloneBrush(zone.ZoneBrush, isSelected ? 0.6 : 0.35);
                Brush outlineBrush = zone.OutlineBrush != null ? CloneBrush(zone.OutlineBrush, 1.0) : CloneBrush(zone.ZoneBrush, 1.0);
                if (isSelected)
                    outlineBrush = CloneBrush(zone.ZoneBrush, 1.0);

                Draw.Rectangle(this, rectTag, false, startBarsAgo, zone.UpperPrice, endBarsAgo, zone.LowerPrice, outlineBrush, fillBrush, isSelected ? 90 : 60);

                string text = string.Format(CultureInfo.InvariantCulture, "{0}\nBid: {1:N0}\nAsk: {2:N0}\nΔ: {3:N0}",
                    zone.Side, zone.BidVolume, zone.AskVolume, zone.Delta);

                Draw.Text(this, textTag, false, text, startBarsAgo, zone.UpperPrice + TickSize, 0, CloneBrush(zone.ZoneBrush, 1.0),
                    new SimpleFont("Segoe UI", 12), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
            }
        }

        private void CleanupZones()
        {
            if (liquidityZones.Count == 0)
                return;

            double acceptanceTicks = AcceptanceTolerance <= 0 ? 1 : AcceptanceTolerance;

            for (int i = liquidityZones.Count - 1; i >= 0; i--)
            {
                LiquidityZone zone = liquidityZones[i];
                bool expired = CurrentBar - zone.StartBar > ZoneLifespan;
                bool accepted = Close[0] > zone.UpperPrice + (acceptanceTicks * TickSize) && zone.Side == "Sell"
                                || Close[0] < zone.LowerPrice - (acceptanceTicks * TickSize) && zone.Side == "Buy";

                if (expired || accepted)
                {
                    RemoveDrawObject(string.Format(CultureInfo.InvariantCulture, "SAL_ZONE_{0}_RECT", zone.StartBar));
                    RemoveDrawObject(string.Format(CultureInfo.InvariantCulture, "SAL_ZONE_{0}_TEXT", zone.StartBar));
                    if (selectedZone == zone)
                    {
                        selectedZone = null;
                        RemoveDrawObject("SAL_SELECTED");
                    }
                    liquidityZones.RemoveAt(i);
                }
            }
        }

        public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor chartAnchor, MouseButtonEventArgs e)
        {
            base.OnMouseDown(chartControl, chartPanel, chartScale, chartAnchor, e);

            if (liquidityZones.Count == 0)
                return;

            Point localPoint = e.GetPosition(chartPanel);
            double price = chartScale.GetValueByY((float)localPoint.Y);
            int rawBarIndex = chartControl.GetBarIdxByX((int)e.GetPosition(chartControl).X);
            int barIndex = Math.Max(0, Math.Min(CurrentBar, rawBarIndex));

            bool matched = false;

            foreach (LiquidityZone zone in liquidityZones)
            {
                if (zone.Contains(barIndex, price))
                {
                    selectedZone = zone;
                    HighlightZone(zone);
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                selectedZone = null;
                RemoveDrawObject("SAL_SELECTED");
                RenderZones();
            }
        }

        private void HighlightZone(LiquidityZone zone)
        {
            if (zone == null)
                return;

            RenderZones();

            string tooltip = string.Format(CultureInfo.InvariantCulture,
                "{0} Liquidity Zone\nBid: {1:N0}\nAsk: {2:N0}\nDelta: {3:N0}",
                zone.Side, zone.BidVolume, zone.AskVolume, zone.Delta);
            Draw.TextFixed(this, "SAL_SELECTED", tooltip, TextPosition.BottomRight, CloneBrush(zone.ZoneBrush, 1.0), new SimpleFont("Segoe UI", 14),
                Brushes.Transparent, Brushes.Transparent, 0);
        }

        private double ResolveBidVolumeValue()
        {
            double value = 0;
            try
            {
                value = GetCurrentBidVolume();
            }
            catch
            {
                value = 0;
            }

            return value;
        }

        private double ResolveAskVolumeValue()
        {
            double value = 0;
            try
            {
                value = GetCurrentAskVolume();
            }
            catch
            {
                value = 0;
            }

            return value;
        }

        private static Brush CloneBrush(Brush input, double opacity)
        {
            if (input == null)
                return Brushes.Transparent;

            Brush clone = input.Clone();
            clone.Opacity = Math.Max(0, Math.Min(1, opacity));
            if (clone.CanFreeze)
                clone.Freeze();
            return clone;
        }

        private void ApplyContractPreset()
        {
            if (UseMiniPreset && !UseMicroPreset)
            {
                ModerateThreshold = 0.18;
                StrongThreshold = 0.35;
                ExtremeThreshold = 0.55;
                MinimumVolume = Math.Max(MinimumVolume, 800);
            }
            else if (UseMicroPreset && !UseMiniPreset)
            {
                ModerateThreshold = 0.12;
                StrongThreshold = 0.25;
                ExtremeThreshold = 0.40;
                MinimumVolume = Math.Min(Math.Max(MinimumVolume, 250), 400);
            }
        }

        private class LiquidityZone
        {
            public int StartBar { get; set; }
            public double PriceCenter { get; set; }
            public double UpperPrice { get; set; }
            public double LowerPrice { get; set; }
            public double BidVolume { get; set; }
            public double AskVolume { get; set; }
            public double Delta { get; set; }
            public string Side { get; set; }
            public Brush ZoneBrush { get; set; }
            public Brush OutlineBrush { get; set; }
            public string Label { get; set; }

            public bool Contains(int barIndex, double price)
            {
                return price <= UpperPrice && price >= LowerPrice;
            }
        }

        private class LiquidityZoneColorProfile
        {
            public Brush ZoneBrush { get; set; }
            public Brush HighlightBrush { get; set; }
            public string Side { get; set; }
            public string Label { get; set; }
            public double Delta { get; set; }
            public double ImbalanceRatio { get; set; }
        }
    }
}
