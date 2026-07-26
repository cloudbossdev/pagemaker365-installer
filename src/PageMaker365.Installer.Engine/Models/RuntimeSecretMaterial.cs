using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;

namespace PageMaker365.Installer.Engine.Models;

public sealed class RuntimeSecretMaterial : IDisposable
{
    private SecureString? _value;

    public RuntimeSecretMaterial(RuntimeSecretInfo definition, SecureString value)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        ArgumentNullException.ThrowIfNull(value);
        _value = value.Copy();
        _value.MakeReadOnly();
    }

    public RuntimeSecretInfo Definition { get; }
    public int Length => _value?.Length ?? 0;

    public static RuntimeSecretMaterial Generate(RuntimeSecretInfo definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var characterCount = Math.Max(64, definition.MinimumLength);
        var byteCount = (characterCount + 1) / 2;
        var randomBytes = RandomNumberGenerator.GetBytes(byteCount);
        var value = new SecureString();
        try
        {
            const string hex = "0123456789abcdef";
            foreach (var item in randomBytes)
            {
                value.AppendChar(hex[item >> 4]);
                value.AppendChar(hex[item & 0x0f]);
            }

            value.MakeReadOnly();
            return new RuntimeSecretMaterial(definition, value);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(randomBytes);
            value.Dispose();
        }
    }

    public static bool IsPrintableAscii(SecureString value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var pointer = IntPtr.Zero;
        try
        {
            pointer = Marshal.SecureStringToGlobalAllocUnicode(value);
            for (var index = 0; index < value.Length; index++)
            {
                var character = (char)Marshal.ReadInt16(pointer, index * sizeof(char));
                if (character is < ' ' or > '~')
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            if (pointer != IntPtr.Zero)
            {
                Marshal.ZeroFreeGlobalAllocUnicode(pointer);
            }
        }
    }

    public void WriteUtf8Value(Stream stream)
    {
        ObjectDisposedException.ThrowIf(_value is null, this);
        ArgumentNullException.ThrowIfNull(stream);

        var pointer = IntPtr.Zero;
        try
        {
            pointer = Marshal.SecureStringToGlobalAllocUnicode(_value!);
            for (var index = 0; index < _value!.Length; index++)
            {
                var character = (char)Marshal.ReadInt16(pointer, index * sizeof(char));
                if (character is < ' ' or > '~')
                {
                    throw new InvalidOperationException("Runtime secret values must contain printable ASCII characters only.");
                }

                stream.WriteByte((byte)character);
            }
        }
        finally
        {
            if (pointer != IntPtr.Zero)
            {
                Marshal.ZeroFreeGlobalAllocUnicode(pointer);
            }
        }
    }

    public void Dispose()
    {
        _value?.Dispose();
        _value = null;
    }
}
