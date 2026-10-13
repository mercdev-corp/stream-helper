using System.Windows.Forms;
using StreamHelper.Shared.UI;

namespace StreamHelper.Tests;

[TestClass]
public sealed class AudioDetectionSettingsControllerTests
{
    [TestMethod]
    public void Controller_InitializesControlsAndLabels_WithLagWindow()
    {
        using var chk = new CheckBox();
        using var trkTolerance = new TrackBar { Minimum = 0, Maximum = 40 };
        using var lblTolerance = new Label();
        using var trkLag = new TrackBar { Minimum = 0, Maximum = 30 };
        using var lblLag = new Label();

        var controller = new AudioDetectionSettingsController(
            chk,
            trkTolerance,
            lblTolerance,
            trkLag,
            lblLag,
            initialSimpleMode: false,
            initialToleranceDb: 15.0,
            initialLagWindowSeconds: 1.5);

        Assert.IsFalse(chk.Checked);
        Assert.IsTrue(trkTolerance.Enabled);
        Assert.AreEqual(15, trkTolerance.Value);
        Assert.AreEqual("15 dB", lblTolerance.Text);

        Assert.IsTrue(trkLag.Enabled);
        Assert.AreEqual(15, trkLag.Value);
        Assert.AreEqual("1.5 s", lblLag.Text);
        Assert.AreEqual(15.0, controller.SavedTolerance);
        Assert.AreEqual(1.5, controller.SavedLagWindow);
    }

    [TestMethod]
    public void Controller_SimpleMode_DisablesTolerance_KeepsLagWindowEnabled()
    {
        using var chk = new CheckBox();
        using var trkTolerance = new TrackBar { Minimum = 0, Maximum = 40 };
        using var lblTolerance = new Label();
        using var trkLag = new TrackBar { Minimum = 0, Maximum = 30 };
        using var lblLag = new Label();

        bool? savedSimple = null;
        double? savedTol = null;
        double? savedLag = null;
        double? changedTol = null;
        bool? changedSimple = null;
        double? changedLag = null;

        var controller = new AudioDetectionSettingsController(
            chk,
            trkTolerance,
            lblTolerance,
            trkLag,
            lblLag,
            initialSimpleMode: false,
            initialToleranceDb: 18.0,
            initialLagWindowSeconds: 1.2,
            saveSimpleMode: s => savedSimple = s,
            saveTolerance: t => savedTol = t,
            saveLagWindow: l => savedLag = l,
            onChanged: (t, s) => { changedTol = t; changedSimple = s; },
            onChangedWithLag: (t, s, l) => { changedTol = t; changedSimple = s; changedLag = l; });

        // Enable simple mode
        chk.Checked = true;

        Assert.IsTrue(savedSimple.HasValue && savedSimple.Value);
        Assert.IsFalse(trkTolerance.Enabled);
        Assert.AreEqual(40, trkTolerance.Value);
        Assert.AreEqual("40 dB", lblTolerance.Text);

        // Lag window must remain enabled in simple mode
        Assert.IsTrue(trkLag.Enabled);
        Assert.AreEqual(12, trkLag.Value);
        Assert.AreEqual("1.2 s", lblLag.Text);

        Assert.AreEqual(18.0, changedTol);
        Assert.IsTrue(changedSimple);
        Assert.AreEqual(1.2, changedLag);

        // Disable simple mode
        chk.Checked = false;
        Assert.IsFalse(savedSimple.Value);
        Assert.IsTrue(trkTolerance.Enabled);
        Assert.AreEqual(18, trkTolerance.Value);
        Assert.AreEqual("18 dB", lblTolerance.Text);
        Assert.IsTrue(trkLag.Enabled);
    }

    [TestMethod]
    public void Controller_ChangingLagWindow_FiresCallbacksAndUpdatesLabel()
    {
        using var chk = new CheckBox();
        using var trkTolerance = new TrackBar { Minimum = 0, Maximum = 40 };
        using var lblTolerance = new Label();
        using var trkLag = new TrackBar { Minimum = 0, Maximum = 30 };
        using var lblLag = new Label();

        double? savedLag = null;
        double? notifiedLag = null;

        var controller = new AudioDetectionSettingsController(
            chk,
            trkTolerance,
            lblTolerance,
            trkLag,
            lblLag,
            initialSimpleMode: false,
            initialToleranceDb: 20.0,
            initialLagWindowSeconds: 1.0,
            saveLagWindow: l => savedLag = l,
            onChangedWithLag: (t, s, l) => notifiedLag = l);

        // Change lag slider to 2.5s (step 25)
        trkLag.Value = 25;

        Assert.AreEqual(2.5, savedLag);
        Assert.AreEqual(2.5, notifiedLag);
        Assert.AreEqual("2.5 s", lblLag.Text);
        Assert.AreEqual(2.5, controller.SavedLagWindow);

        // Change lag slider to 0.0s (step 0)
        trkLag.Value = 0;

        Assert.AreEqual(0.0, savedLag);
        Assert.AreEqual(0.0, notifiedLag);
        Assert.AreEqual("0.0 s", lblLag.Text);
        Assert.AreEqual(0.0, controller.SavedLagWindow);
    }

    [TestMethod]
    public void Controller_ApplyState_UpdatesStateAndPreservesLag()
    {
        using var chk = new CheckBox();
        using var trkTolerance = new TrackBar { Minimum = 0, Maximum = 40 };
        using var lblTolerance = new Label();
        using var trkLag = new TrackBar { Minimum = 0, Maximum = 30 };
        using var lblLag = new Label();

        var controller = new AudioDetectionSettingsController(
            chk,
            trkTolerance,
            lblTolerance,
            trkLag,
            lblLag,
            initialSimpleMode: false,
            initialToleranceDb: 10.0,
            initialLagWindowSeconds: 0.8);

        controller.ApplyState(simpleMode: true, toleranceDb: 25.0, lagWindowSeconds: 2.2);

        Assert.IsTrue(chk.Checked);
        Assert.IsFalse(trkTolerance.Enabled);
        Assert.AreEqual(40, trkTolerance.Value);
        Assert.AreEqual(22, trkLag.Value);
        Assert.AreEqual("2.2 s", lblLag.Text);
        Assert.AreEqual(2.2, controller.SavedLagWindow);
        Assert.AreEqual(25.0, controller.SavedTolerance);
    }
}
