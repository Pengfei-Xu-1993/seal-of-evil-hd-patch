using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SealHdInstaller
{
    internal sealed class LayerValue
    {
        public string Hive { get; set; }
        public string View { get; set; }
        public bool Exists { get; set; }
        public string Value { get; set; }
        public int Kind { get; set; }
        public LayerValue Copy() { return (LayerValue)MemberwiseClone(); }
    }

    internal interface ILayerStore
    {
        LayerValue Read(string hive, string view, string gameExe);
        void EnsureWritable(LayerValue value, string gameExe);
        void Write(LayerValue value, string gameExe);
    }

    internal sealed class WindowsLayerStore : ILayerStore
    {
        private const string KeyPath = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
        public LayerValue Read(string hive, string view, string gameExe)
        {
            using (var basis = RegistryKey.OpenBaseKey(hive == "HKCU" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, view == "32" ? RegistryView.Registry32 : RegistryView.Registry64))
            using (var key = basis.OpenSubKey(KeyPath, false))
            {
                var result = new LayerValue { Hive = hive, View = view };
                if (key == null || !key.GetValueNames().Contains(gameExe, StringComparer.OrdinalIgnoreCase)) return result;
                object value = key.GetValue(gameExe, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (!(value is string)) throw new InvalidDataException("The game has a non-text compatibility setting at " + hive + " (" + view + "-bit view).");
                result.Exists = true; result.Value = (string)value; result.Kind = (int)key.GetValueKind(gameExe);
                return result;
            }
        }
        public void EnsureWritable(LayerValue value, string gameExe)
        {
            try
            {
                using (var basis = RegistryKey.OpenBaseKey(value.Hive == "HKCU" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, value.View == "32" ? RegistryView.Registry32 : RegistryView.Registry64))
                using (var key = basis.OpenSubKey(KeyPath, true))
                    if (key == null && value.Exists) throw new IOException("The compatibility settings key disappeared. Please inspect the game folder again.");
            }
            catch (UnauthorizedAccessException error) { throw NeedsAdmin(value, error); }
            catch (SecurityException error) { throw NeedsAdmin(value, error); }
        }
        public void Write(LayerValue value, string gameExe)
        {
            try
            {
                using (var basis = RegistryKey.OpenBaseKey(value.Hive == "HKCU" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, value.View == "32" ? RegistryView.Registry32 : RegistryView.Registry64))
                using (var key = basis.CreateSubKey(KeyPath))
                {
                    if (value.Exists) key.SetValue(gameExe, value.Value, (RegistryValueKind)value.Kind);
                    else key.DeleteValue(gameExe, false);
                }
            }
            catch (UnauthorizedAccessException error) { throw NeedsAdmin(value, error); }
            catch (SecurityException error) { throw NeedsAdmin(value, error); }
        }
        private static Exception NeedsAdmin(LayerValue value, Exception inner)
        {
            return new UnauthorizedAccessException("Administrator permission is required to update this game's compatibility setting at " + value.Hive + " (" + value.View + "-bit view). Run this installer as administrator.", inner);
        }
    }

    internal static class CompatibilityLayers
    {
        internal static List<LayerValue> ReadAll(ILayerStore store, string exe)
        {
            var values = new List<LayerValue>();
            foreach (string hive in new[] { "HKCU", "HKLM" })
                foreach (string view in Environment.Is64BitOperatingSystem ? new[] { "32", "64" } : new[] { "32" })
                    values.Add(store.Read(hive, view, exe));
            return values;
        }
        internal static List<LayerValue> Clean(List<LayerValue> originals)
        {
            return originals.Select(value =>
            {
                var result = value.Copy();
                if (!result.Exists) return result;
                if (!Regex.IsMatch(result.Value, @"(?<!\S)DWM8And16BitMitigation(?!\S)", RegexOptions.IgnoreCase)) return result;
                result.Value = Regex.Replace(result.Value, @"(?<!\S)DWM8And16BitMitigation(?!\S)", "", RegexOptions.IgnoreCase);
                // '$' and '~' are syntax prefixes, not remaining compatibility flags.
                if (Regex.IsMatch(result.Value, @"^[\s~$]*$")) { result.Exists = false; result.Value = null; result.Kind = 0; }
                return result;
            }).ToList();
        }
        internal static bool Equal(LayerValue a, LayerValue b)
        {
            return a.Hive == b.Hive && a.View == b.View && a.Exists == b.Exists && (!a.Exists || (a.Value == b.Value && a.Kind == b.Kind));
        }
        internal static void Verify(ILayerStore store, string exe, List<LayerValue> expected)
        {
            foreach (var value in expected)
                if (!Equal(value, store.Read(value.Hive, value.View, exe)))
                    throw new IOException("This game's compatibility setting changed after inspection at " + value.Hive + " (" + value.View + "-bit view). Please inspect the game folder again.");
        }
        internal static void Apply(ILayerStore store, string exe, List<LayerValue> before, List<LayerValue> after)
        {
            // Validate all views first: on some Windows versions the views share physical values.
            Verify(store, exe, before);
            for (int i = 0; i < before.Count; ++i)
                if (!Equal(before[i], after[i])) store.Write(after[i], exe);
            Verify(store, exe, after);
        }
    }
}
