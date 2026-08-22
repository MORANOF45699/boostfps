using System.Management;
using System.Net.NetworkInformation;
using BoostFPS.Core.Models;

namespace BoostFPS.Core.Services;

/// <summary>
/// Probes the current machine once so every tweak/service gate has real hardware facts to test against.
/// Nothing here writes; a failed probe degrades to Unknown/false rather than throwing.
/// </summary>
public sealed class MachineProfiler
{
    private const string DisplayClassGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string DisplayClassKey =
        @"HKLM\SYSTEM\CurrentControlSet\Control\Class\" + DisplayClassGuid;

    public MachineProfile Probe()
    {
        var cs = QueryFirst("SELECT * FROM Win32_ComputerSystem");
        var cpu = QueryFirst("SELECT * FROM Win32_Processor");
        var os = QueryFirst("SELECT * FROM Win32_OperatingSystem");

        var manufacturer = Str(cs, "Manufacturer");
        var model = Str(cs, "Model");
        var isVm = LooksVirtual(manufacturer, model);

        var (gpuVendor, gpuName, gpuIndex) = ProbeGpu();

        return new MachineProfile
        {
            CpuVendor = Str(cpu, "Manufacturer") switch
            {
                var m when m.Contains("Intel", StringComparison.OrdinalIgnoreCase) => CpuVendor.Intel,
                var m when m.Contains("AMD", StringComparison.OrdinalIgnoreCase)
                        || m.Contains("Authentic", StringComparison.OrdinalIgnoreCase) => CpuVendor.Amd,
                _ => CpuVendor.Unknown
            },
            CpuName = Str(cpu, "Name").Trim(),
            PhysicalCores = (int)Num(cpu, "NumberOfCores"),
            LogicalCores = Environment.ProcessorCount,

            GpuVendor = gpuVendor,
            GpuName = gpuName,
            GpuClassKeyIndex = gpuIndex,

            TotalMemoryBytes = Num(cs, "TotalPhysicalMemory"),
            Chassis = isVm ? ChassisKind.VirtualMachine : ProbeChassis(),
            SystemDriveIsSsd = ProbeSystemDriveIsSsd(),
            DomainJoined = Bool(cs, "PartOfDomain"),
            IsVirtualMachine = isVm,
            WindowsBuild = int.TryParse(Str(os, "BuildNumber"), out var b) ? b : 0,
            WindowsCaption = Str(os, "Caption").Trim(),

            HasPhysicalPrinter = ProbePhysicalPrinter(),
            HasBluetooth = HasPnpClass("Bluetooth"),
            HasTouchOrPen = HasPnpClass("HIDClass", "touch", "digitizer", "pen"),
            HasFingerprintReader = HasPnpClass("Biometric"),
            HasSmartCardReader = HasPnpClass("SmartCardReader"),
            HasWifi = NetworkInterface.GetAllNetworkInterfaces()
                .Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211),
            HasSshConfig = Directory.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh")),

            ActiveNetInterfaceGuids = ProbeActiveInterfaceGuids(),
            UsbClassKeys = RegistryPath.SubKeyNames(@"HKLM\SYSTEM\CurrentControlSet\Services\Class\USB")
        };
    }

    // --- individual probes -------------------------------------------------

    private static (GpuVendor, string, string?) ProbeGpu()
    {
        // Prefer the adapter that has a driver key under Control\Class, so tweaks that
        // target the class key land on the right index instead of a hardcoded 0000.
        foreach (var index in RegistryPath.SubKeyNames(DisplayClassKey).Where(n => n.Length == 4 && n.All(char.IsDigit)))
        {
            using var key = RegistryPath.OpenRead($@"{DisplayClassKey}\{index}");
            if (key is null) continue;

            var desc = key.GetValue("DriverDesc") as string ?? "";
            var provider = key.GetValue("ProviderName") as string ?? "";
            var vendor = Classify(desc + " " + provider);
            if (vendor != GpuVendor.Unknown)
                return (vendor, desc, index);
        }

        var vc = QueryFirst("SELECT * FROM Win32_VideoController");
        var name = Str(vc, "Name");
        return (Classify(name), name, null);

        static GpuVendor Classify(string s) =>
            s.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Nvidia :
            s.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Amd :
            s.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Intel :
            GpuVendor.Unknown;
    }

    private static ChassisKind ProbeChassis()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
            foreach (ManagementObject mo in searcher.Get())
            {
                if (mo["ChassisTypes"] is not ushort[] types) continue;
                foreach (var t in types)
                {
                    // 8 portable, 9 laptop, 10 notebook, 11 hand held, 12 docking station,
                    // 14 sub notebook, 30 tablet, 31 convertible, 32 detachable
                    if (t is 8 or 9 or 10 or 11 or 12 or 14 or 30 or 31 or 32) return ChassisKind.Laptop;
                }
            }
        }
        catch { /* WMI unavailable - fall through */ }

        // Battery present is a reliable second opinion when the enclosure lies.
        try
        {
            using var bat = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
            if (bat.Get().Count > 0) return ChassisKind.Laptop;
        }
        catch { }

        return ChassisKind.Desktop;
    }

    private static bool ProbeSystemDriveIsSsd()
    {
        var sysLetter = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\');
        if (string.IsNullOrEmpty(sysLetter)) return true;

        try
        {
            // MSFT_PhysicalDisk.MediaType: 3 HDD, 4 SSD, 5 SCM. Map partition -> disk -> media type.
            using var partSearch = new ManagementObjectSearcher(
                @"\\.\root\microsoft\windows\storage",
                $"SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter='{sysLetter[0]}'");

            foreach (ManagementObject part in partSearch.Get())
            {
                var diskNumber = Convert.ToUInt32(part["DiskNumber"]);
                using var diskSearch = new ManagementObjectSearcher(
                    @"\\.\root\microsoft\windows\storage",
                    $"SELECT MediaType FROM MSFT_PhysicalDisk WHERE DeviceId='{diskNumber}'");

                foreach (ManagementObject disk in diskSearch.Get())
                {
                    var media = Convert.ToUInt16(disk["MediaType"]);
                    if (media == 3) return false;   // spinning
                    if (media is 4 or 5) return true;
                }
            }
        }
        catch { /* storage namespace missing (very old builds / some VMs) */ }

        return true; // assume SSD: the tweaks gated on this are the risky-on-HDD ones
    }

    private static bool ProbePhysicalPrinter()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, PortName FROM Win32_Printer");
            foreach (ManagementObject p in searcher.Get())
            {
                var name = (p["Name"] as string ?? "").ToLowerInvariant();
                var port = (p["PortName"] as string ?? "").ToLowerInvariant();

                var virtualPrinter =
                    name.Contains("onenote") || name.Contains("microsoft print to pdf") ||
                    name.Contains("microsoft xps") || name.Contains("fax") ||
                    port.StartsWith("portprompt") || port.StartsWith("shrfax") || port.StartsWith("nul");

                if (!virtualPrinter) return true;
            }
        }
        catch { }
        return false;
    }

    private static bool HasPnpClass(string pnpClass, params string[] nameContains)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT Name FROM Win32_PnPEntity WHERE PNPClass='{pnpClass}'");

            foreach (ManagementObject d in searcher.Get())
            {
                if (nameContains.Length == 0) return true;
                var name = (d["Name"] as string ?? "").ToLowerInvariant();
                if (nameContains.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase))) return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Real Ethernet/Wi-Fi adapters only. Windows also registers loopback, tunnels and a long
    /// tail of WAN miniports under Tcpip\Parameters\Interfaces; writing latency values into those
    /// does nothing useful and makes a tweak look "Partial" forever.
    /// </summary>
    private static List<string> ProbeActiveInterfaceGuids()
    {
        const string interfaces = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                                              or NetworkInterfaceType.GigabitEthernet
                                              or NetworkInterfaceType.FastEthernetT
                                              or NetworkInterfaceType.FastEthernetFx
                                              or NetworkInterfaceType.Wireless80211)
            .Select(n => n.Id)
            .Where(id => id.StartsWith('{') && RegistryPath.KeyExists($@"{interfaces}\{id}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // --- WMI helpers -------------------------------------------------------

    private static ManagementObject? QueryFirst(string wql)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            foreach (ManagementObject mo in searcher.Get()) return mo;
        }
        catch { }
        return null;
    }

    private static string Str(ManagementBaseObject? mo, string prop)
    {
        try { return mo?[prop]?.ToString() ?? ""; } catch { return ""; }
    }

    private static ulong Num(ManagementBaseObject? mo, string prop)
    {
        try { return mo?[prop] is null ? 0 : Convert.ToUInt64(mo[prop]); } catch { return 0; }
    }

    private static bool Bool(ManagementBaseObject? mo, string prop)
    {
        try { return mo?[prop] is not null && Convert.ToBoolean(mo[prop]); } catch { return false; }
    }

    private static bool LooksVirtual(string manufacturer, string model)
    {
        var s = (manufacturer + " " + model).ToLowerInvariant();
        return s.Contains("vmware") || s.Contains("virtualbox") || s.Contains("kvm")
            || s.Contains("qemu") || s.Contains("xen") || s.Contains("virtual machine")
            || s.Contains("hyper-v") || s.Contains("parallels");
    }
}
