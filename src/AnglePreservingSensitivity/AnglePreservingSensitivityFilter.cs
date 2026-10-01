using System;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace AnglePreservingSensitivity
{
    /// <summary>
    /// For relative output mode. A lower vertical sensitivity set in OTD (X:Y) bends diagonal movements
    /// (45 degrees comes out flatter). With OTD's X and Y sensitivity set equal, this filter applies the vertical
    /// slowdown instead while keeping every movement's direction. It works on each report's movement on its own:
    /// no smoothing, no history, no added latency.
    /// </summary>
    [PluginName("Angle-Preserving Sensitivity")]
    public sealed class AnglePreservingSensitivityFilter : IPositionedPipelineElement<IDeviceReport>
    {
        public event Action<IDeviceReport>? Emit;

        // After the output mode's transform, relative mode reports carry the movement since the previous report in
        // screen pixels (rotation and sensitivity already applied).
        public PipelinePosition Position => PipelinePosition.PostTransform;

        public void Consume(IDeviceReport value)
        {
            if (value is IAbsolutePositionReport report && VerticalSpeed != 100)
                report.Position = DirectionalScale.Apply(report.Position, (float)(VerticalSpeed / 100));

            Emit?.Invoke(value);
        }

        [Property("Vertical Speed"), DefaultPropertyValue(100.0), Unit("%"), ToolTip(
            "Relative mode only. Set OTD's X and Y sensitivity to the same value (your horizontal one), then set this\n" +
            "to vertical / horizontal sensitivity, e.g. 20.617 / 28.589 = 72.1%.\n" +
            "Vertical movement gets this share of the speed, horizontal keeps 100%, diagonals get what a per-axis ratio\n" +
            "would give them, but every movement keeps its direction. 100 = off.")]
        public double VerticalSpeed { set; get; } = 100;
    }
}
