using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;

namespace SolidWorks.TaskpaneCalculator.Services
{
    // Minimal wrapper to read Generic Credentials from Windows Credential Manager
    public static class CredentialManager
    {
        private const int CRED_TYPE_GENERIC = 1;

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern void CredFree([In] IntPtr cred);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public long LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        // Read a generic credential by target name and return the blob as UTF8 string
        public static string ReadGenericSecret(string targetName)
        {
            if (string.IsNullOrWhiteSpace(targetName)) return null;
            if (!CredRead(targetName, CRED_TYPE_GENERIC, 0, out var credPtr) || credPtr == IntPtr.Zero) return null;
            try
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize <= 0) return null;
                var blob = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, blob, 0, cred.CredentialBlobSize);
                // Credential blob is typically stored as Unicode or UTF8; try UTF8 first then Unicode
                try { return Encoding.UTF8.GetString(blob).TrimEnd('\0'); } catch { }
                try { return Encoding.Unicode.GetString(blob).TrimEnd('\0'); } catch { }
                return null;
            }
            finally
            {
                CredFree(credPtr);
            }
        }

        // Store a generic credential using cmdkey.exe (avoids complex P/Invoke write code)
        // Returns true on success.
        public static bool WriteGenericSecret(string targetName, string secret, string user = "unused")
        {
            if (string.IsNullOrWhiteSpace(targetName) || secret == null) return false;
            try
            {
                // Use cmdkey to create a generic credential entry.
                var psi = new ProcessStartInfo
                {
                    FileName = "cmdkey",
                    Arguments = $"/generic:{EscapeArg(targetName)} /user:{EscapeArg(user)} /pass:{EscapeArg(secret)}",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(5000);
                    return p.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string EscapeArg(string s)
        {
            if (s == null) return string.Empty;
            return s.IndexOf(' ') >= 0 ? "\"" + s.Replace("\"", "\\\"") + "\"" : s;
        }
    }
}
