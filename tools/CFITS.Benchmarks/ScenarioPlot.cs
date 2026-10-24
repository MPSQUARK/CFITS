using System.Globalization;
using System.Text;
using BenchmarkDotNet.Reports;
using ScottPlot;

namespace CFITS.Benchmarks;

internal static class ScenarioPlot
{
    public static string Save(Summary summary, string pngPath)
    {
        var rows = summary.Reports
            .Where(report => report.Success && report.ResultStatistics is not null)
            .Select(report =>
            {
                var stats = report.ResultStatistics!;
                string method = report.BenchmarkCase.Descriptor.WorkloadMethod.Name;
                return new Measurement(FitsScenarios.Label(method), stats.Mean / 1_000_000d, stats.StandardDeviation / 1_000_000d);
            })
            .Where(row => row.Label != FitsScenarios.Label(nameof(FitsScenarios.ReadSdssSpectrum)) || row.MeanMilliseconds > 0.001)
            .OrderBy(row => row.MeanMilliseconds)
            .ToArray();

        if (rows.Length < 10)
        {
            throw new InvalidOperationException($"Expected at least 10 scenarios, got {rows.Length}.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);
        var plot = new Plot();
        plot.FigureBackground.Color = Colors.White;
        plot.DataBackground.Color = Colors.White;
        plot.Axes.Color(Color.FromHex("#7b8da6"));
        plot.Grid.MajorLineColor = Color.FromHex("#d8e0ec").WithAlpha(0.8);
        plot.Legend.BackgroundColor = Colors.White.WithAlpha(0.9);
        plot.Legend.OutlineColor = Color.FromHex("#d8e0ec");

        var bars = new Bar[rows.Length];
        for (int index = 0; index < rows.Length; index++)
        {
            bars[index] = new Bar
            {
                Position = index,
                Value = rows[index].MeanMilliseconds,
                Error = rows[index].StdDevMilliseconds,
                Size = 0.7,
                ErrorSize = 0.18,
            };
        }

        var series = plot.Add.Bars(bars);
        series.Horizontal = true;
        series.Color = Color.FromHex("#0d8f8a");
        series.LegendText = "Mean ± 1 standard deviation";

        double[] positions = Enumerable.Range(0, rows.Length).Select(i => (double)i).ToArray();
        string[] labels = rows.Select(row => row.Label).ToArray();
        plot.Axes.Left.SetTicks(positions, labels);
        plot.Axes.Left.TickLabelStyle.FontSize = 13;
        plot.Axes.Bottom.Label.Text = "Mean time (ms)";
        plot.Axes.Bottom.Label.FontSize = 14;
        plot.Axes.Title.Label.Text = "CFITS baseline scenarios";
        plot.Axes.Title.Label.FontSize = 18;
        plot.Axes.Margins(left: 0, right: 0.08, bottom: 0.04, top: 0.04);
        plot.ShowLegend(Alignment.LowerRight);
        plot.SavePng(pngPath, 1100, 760);

        string csvPath = Path.ChangeExtension(pngPath, ".csv");
        var csv = new StringBuilder();
        csv.AppendLine("scenario,mean_ms,stddev_ms");
        foreach (var row in rows)
        {
            csv.Append(row.Label).Append(',').Append(row.MeanMilliseconds.ToString("G17", CultureInfo.InvariantCulture)).Append(',').AppendLine(row.StdDevMilliseconds.ToString("G17", CultureInfo.InvariantCulture));
        }

        File.WriteAllText(csvPath, csv.ToString());
        return csvPath;
    }

    private readonly record struct Measurement(string Label, double MeanMilliseconds, double StdDevMilliseconds);
}
