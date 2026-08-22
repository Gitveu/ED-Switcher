using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace EDAccountSwitcher.Core
{
    public static class MachineId
    {
        private const string MachinePath = @"SOFTWARE\Microsoft\Cryptography";
        private const string FrontierPath = @"SOFTWARE\Frontier Developments\Cryptography";
        private const string Key = "MachineGuid";

        public static string MakeId(string machineId, string frontierId)
        {
            var concat = machineId.Trim() + frontierId.Trim();
            var hash = SHA1.HashData(Encoding.ASCII.GetBytes(concat));

            string hex = BitConverter.ToString(hash).Replace("-", "");
            return hex.Substring(0, Math.Min(16, hex.Length)).ToLowerInvariant();
        }

        public static void EnsureIdsExist()
        {
            using var regKey = Registry.CurrentUser.OpenSubKey(FrontierPath);
            if (regKey?.GetValue(Key) != null)
                return;

            using var created = Registry.CurrentUser.CreateSubKey(FrontierPath, RegistryKeyPermissionCheck.ReadWriteSubTree);
            created.SetValue(Key, Guid.NewGuid().ToString());
        }

        public static string GetId()
        {
            EnsureIdsExist();
            using var machineKey = Registry.LocalMachine.OpenSubKey(MachinePath)
                ?? throw new InvalidOperationException($"Unable to open HKLM\\{MachinePath}");
            using var frontierKey = Registry.CurrentUser.OpenSubKey(FrontierPath)
                ?? throw new InvalidOperationException($"Unable to open HKCU\\{FrontierPath}");

            var machineId = machineKey.GetValue(Key)?.ToString()
                ?? throw new InvalidOperationException($"Unable to read {Key} from HKLM\\{MachinePath}");
            var frontierId = frontierKey.GetValue(Key)?.ToString()
                ?? throw new InvalidOperationException($"Unable to read {Key} from HKCU\\{FrontierPath}");

            return MakeId(machineId, frontierId);
        }
    }
}