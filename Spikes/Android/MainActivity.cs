using Android.Util;
using Android.Widget;
using Axiom.Mesh.Spikes;

namespace Axiom.Mesh.Spike.Android;

/// <summary>Runs the two spike scenarios on the phone and shows the log; the same lines go to logcat under the tag "AxiomMesh".</summary>
[Activity(Label = "Axiom.Mesh spike", MainLauncher = true)]
public sealed class MainActivity : Activity
{
    private const string Tag = "AxiomMesh";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var text = new TextView(this) { TextSize = 11, Typeface = global::Android.Graphics.Typeface.Monospace };
        var scroll = new ScrollView(this);
        scroll.AddView(text);
        SetContentView(scroll);

        var log = new SpikeLog(line =>
        {
            Log.Info(Tag, line);
            RunOnUiThread(() => text.Append(line + "\n"));
        });

        _ = Task.Run(async () =>
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            bool adoption = await AdoptionScenario.RunAsync(41901, log, stop.Token);
            string directory = Path.Combine(FilesDir!.AbsolutePath, "ldb");
            bool ldb = await LdbScenario.RunAsync(directory, log, stop.Token);
            log.Add("");
            log.Add($"outbound adoption: {(adoption ? "PASS" : "FAIL")}");
            log.Add($"LDB on Android: {(ldb ? "PASS" : "FAIL")}");
        });
    }
}
