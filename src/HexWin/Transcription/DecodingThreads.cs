using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace HexWin.Transcription;

/// <summary>
/// Turns the <c>threads</c> setting into the count handed to the engine.
///
/// <para><b>Zero means automatic: one thread per physical core</b>, not per
/// logical processor. The two threads of a hyper-threaded core share the same
/// arithmetic units, which the matrix products of the model already keep busy;
/// a second thread on it only adds synchronisation. ONNX Runtime picks the
/// same count when left to itself.</para>
///
/// <para>A fixed four used to be the default: too many on a dual-core laptop,
/// where decoding then fights the rest of the machine, too few on a desktop
/// with eight cores or more.</para>
/// </summary>
public static partial class DecodingThreads
{
    /// <summary>
    /// Ceiling of the automatic count. The model is small: past a handful of
    /// threads, splitting the work costs more than it brings.
    /// </summary>
    internal const int AutomaticCeiling = 8;

    public static int Resolve(int setting) => Resolve(setting, PhysicalCores());

    public static int Resolve(int setting, int physicalCores) =>
        setting > 0 ? setting : Math.Clamp(physicalCores, 1, AutomaticCeiling);

    [ExcludeFromCodeCoverage(Justification = "Asks Windows; the decision itself is in Resolve, which is tested.")]
    private static int PhysicalCores()
    {
        uint length = 0;
        GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref length);

        if (length == 0)
        {
            return Environment.ProcessorCount;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)length);

        try
        {
            if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref length))
            {
                return Environment.ProcessorCount;
            }

            // One variable-size record per core: its relationship on four
            // bytes, then its own size, which leads to the next one.
            int cores = 0;
            int offset = 0;

            while (offset < length)
            {
                int size = Marshal.ReadInt32(buffer, offset + sizeof(int));

                if (size <= 0)
                {
                    break;
                }

                cores++;
                offset += size;
            }

            return cores > 0 ? cores : Environment.ProcessorCount;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private const int RelationProcessorCore = 0;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);
}
