using System.Runtime.InteropServices;
using System.Security;

namespace Lumos.Desktop.Common;

/// <summary>
/// Marshalling helpers for SecureString.
///
/// An honest note on what this can and cannot do: .NET strings are immutable
/// and moved around by the garbage collector, so the moment a password becomes
/// a `string` it may exist in several places in memory that we can never
/// overwrite. SecureString narrows the window but does not close it. The
/// helpers here at least guarantee the unmanaged BSTR is zeroed and freed
/// rather than left on the native heap.
///
/// Consolidated here because three view models had grown their own identical
/// copies of this code.
/// </summary>
public static class SecureStringHelper
{
    /// <summary>
    /// Marshal a SecureString to a managed string. The caller should use it and
    /// drop the reference as soon as possible.
    /// </summary>
    public static string ToPlainText(SecureString secure)
    {
        ArgumentNullException.ThrowIfNull(secure);

        var bstr = IntPtr.Zero;
        try
        {
            bstr = Marshal.SecureStringToBSTR(secure);
            return Marshal.PtrToStringBSTR(bstr);
        }
        finally
        {
            if (bstr != IntPtr.Zero) Marshal.ZeroFreeBSTR(bstr);
        }
    }

    /// <summary>Compare two SecureStrings without materialising either as a managed string.</summary>
    public static bool AreEqual(SecureString a, SecureString b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a.Length != b.Length) return false;

        var bstrA = IntPtr.Zero;
        var bstrB = IntPtr.Zero;
        try
        {
            bstrA = Marshal.SecureStringToBSTR(a);
            bstrB = Marshal.SecureStringToBSTR(b);

            var equal = true;
            for (var i = 0; i < a.Length; i++)
            {
                var charA = Marshal.ReadInt16(bstrA, i * 2);
                var charB = Marshal.ReadInt16(bstrB, i * 2);
                // No early exit: keep the comparison time independent of where
                // the first difference falls.
                if (charA != charB) equal = false;
            }
            return equal;
        }
        finally
        {
            if (bstrA != IntPtr.Zero) Marshal.ZeroFreeBSTR(bstrA);
            if (bstrB != IntPtr.Zero) Marshal.ZeroFreeBSTR(bstrB);
        }
    }
}
