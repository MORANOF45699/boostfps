using System.Windows;
using System.Windows.Controls;
using BoostFPS.Core.Models;
using BoostFPS.Core.Services;

namespace BoostFPS.Views;

public partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
        Load();
    }

    private void Load()
    {
        var engine = App.Engine;
        var m = engine.Machine;

        MachineFacts.ItemsSource = new List<KeyValuePair<string, string>>
        {
            new("CPU", $"{m.CpuName}  ({m.PhysicalCores} cores / {m.LogicalCores} threads)"),
            new("GPU", $"{m.GpuName}  [{m.GpuVendor}]"),
            new("RAM", $"{m.TotalMemoryBytes / 1024.0 / 1024 / 1024:F1} GB"),
            new("Windows", $"{m.WindowsCaption}  build {m.WindowsBuild}"),
            new("ชนิดเครื่อง", m.Chassis switch
            {
                ChassisKind.Laptop => "โน้ตบุ๊ก (tweak ที่กินแบตถูกซ่อน)",
                ChassisKind.VirtualMachine => "Virtual machine",
                _ => "เดสก์ท็อป"
            }),
            new("ไดรฟ์ระบบ", m.SystemDriveIsSsd ? "SSD / NVMe" : "HDD (ไม่ปิด SysMain และ prefetch)"),
            new("Domain", m.DomainJoined ? "อยู่ใน domain" : "เครื่องเดี่ยว"),
            new("อุปกรณ์ที่ตรวจพบ", Devices(m)),
            new("Network interfaces", $"{m.ActiveNetInterfaceGuids.Count} adapter"),
            new("USB class keys", $"{m.UsbClassKeys.Count} รายการ")
        };

        var tweaks = engine.AvailableTweaks();
        var on = tweaks.Count(t => engine.Tweaks.GetStatus(t) == TweakStatus.On);
        var partial = tweaks.Count(t => engine.Tweaks.GetStatus(t) == TweakStatus.Partial);
        var services = engine.AvailableServices();
        var disabled = services.Count(s => s.IsDisabled);
        var gated = services.Count(s => !s.CanToggle);

        StatusText.Text =
            $"Registry tweaks ที่ใช้ได้บนเครื่องนี้: {tweaks.Count} จากทั้งหมด {Catalog.Tweaks.Count}\n" +
            $"เปิดอยู่แล้ว: {on}  •  เปิดบางส่วน: {partial}\n" +
            $"Services ที่แสดง: {services.Count}  •  ปิดอยู่แล้ว: {disabled}  •  ถูก gate ตัด: {gated}\n" +
            $"Backup snapshot ที่เก็บไว้: {engine.Backups.List().Count}";

        RefreshProtection();
        RefreshAutoTune();
    }

    private void RefreshAutoTune()
    {
        var plan = App.Engine.AutoTune.Recommend();
        var pendingTweaks = plan.Tweaks.Count(t => App.Engine.Tweaks.GetStatus(t) != TweakStatus.On);
        var pendingServices = plan.Services.Count;

        AutoTuneRecommendation.Text =
            $"แนะนำ: ชุด {plan.Tier}\n{plan.Reason}\n\n" +
            $"จะเปิด {plan.Tweaks.Count} tweak (ยังต้องทำ {pendingTweaks}) และปิด {pendingServices} service\n" +
            "สร้าง restore point + backup ค่าเดิมก่อนทุกอย่าง revert ได้จากหน้า Backup";
    }

    private void AutoTune_Click(object sender, RoutedEventArgs e)
    {
        var plan = App.Engine.AutoTune.Recommend();

        var confirm = MessageBox.Show(
            $"ปรับแต่งเครื่องนี้อัตโนมัติ (ชุด {plan.Tier})?\n\n" +
            $"{plan.Reason}\n\n" +
            $"- เปิด {plan.Tweaks.Count} registry tweak\n" +
            $"- ปิด {plan.Services.Count} service\n" +
            "- สร้าง restore point + backup ก่อนเสมอ\n\n" +
            "บาง tweak มีผลหลังรีสตาร์ท",
            "Auto-Tune", MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK) return;

        AutoTuneButton.IsEnabled = false;
        try
        {
            var result = App.Engine.Apply(new ApplyRequest
            {
                Tweaks = plan.Tweaks,
                Services = plan.Services,
                Description = $"Auto-Tune {plan.Tier}"
            });

            var lines = new List<string>
            {
                $"เปิด tweak {result.TweaksApplied} รายการ",
                $"เปลี่ยน service {result.ServicesChanged} รายการ",
                $"Backup: {result.BackupId}"
            };
            if (result.RebootRequired) lines.Add("ต้องรีสตาร์ทเพื่อให้มีผลครบ");
            if (result.Failures.Count > 0) lines.Add("\nล้มเหลว:\n" + string.Join("\n", result.Failures));

            MessageBox.Show(string.Join("\n", lines), "Auto-Tune เสร็จ",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);

            Load();
        }
        finally { AutoTuneButton.IsEnabled = true; }
    }

    private void OpenBackup_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show("ไปที่แท็บ \"Backup / Revert\" ใน sidebar เพื่อกู้ snapshot", "BoostFPS");

    private static string Devices(MachineProfile m)
    {
        var found = new List<string>();
        if (m.HasPhysicalPrinter) found.Add("เครื่องพิมพ์");
        if (m.HasBluetooth) found.Add("Bluetooth");
        if (m.HasWifi) found.Add("Wi-Fi");
        if (m.HasTouchOrPen) found.Add("ทัชสกรีน/ปากกา");
        if (m.HasFingerprintReader) found.Add("สแกนนิ้ว");
        if (m.HasSmartCardReader) found.Add("สมาร์ทการ์ด");
        if (m.HasSshConfig) found.Add("OpenSSH");
        return found.Count == 0 ? "ไม่พบอุปกรณ์พิเศษ" : string.Join(", ", found);
    }

    private void RefreshProtection()
    {
        var enabled = App.Engine.RestorePoints.IsProtectionEnabled();

        ProtectionText.Text = enabled
            ? "เปิดอยู่ — ทุกครั้งที่กด Apply โปรแกรมจะสร้าง restore point ให้ก่อน"
            : "ปิดอยู่ — ถ้าไม่เปิด จะมีแค่ backup ระดับ registry ไม่มี restore point ให้ย้อนทั้งระบบ";

        EnableProtectionButton.IsEnabled = !enabled;
    }

    private void EnableProtection_Click(object sender, RoutedEventArgs e)
    {
        EnableProtectionButton.IsEnabled = false;
        var ok = App.Engine.RestorePoints.TryEnableProtection();

        MessageBox.Show(
            ok ? "เปิด System Protection ให้ไดรฟ์ระบบแล้ว" : "เปิดไม่สำเร็จ ลองเปิดเองที่ System Properties > System Protection",
            "System Protection", MessageBoxButton.OK,
            ok ? MessageBoxImage.Information : MessageBoxImage.Warning);

        RefreshProtection();
    }
}
