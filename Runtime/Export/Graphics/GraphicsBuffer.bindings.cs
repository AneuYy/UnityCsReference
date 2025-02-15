// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Scripting;
using UnityEngine.Bindings;

namespace UnityEngine
{
    [NativeHeader("Runtime/GfxDevice/GfxDeviceTypes.h")]
    [NativeClass("GfxBufferID")]
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct GraphicsBufferHandle : IEquatable<GraphicsBufferHandle>
    {
        public readonly UInt32 value;

        public override int GetHashCode()
        {
            return value.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is GraphicsBufferHandle)
            {
                return Equals((GraphicsBufferHandle)obj);
            }

            return false;
        }

        public bool Equals(GraphicsBufferHandle other)
        {
            return value == other.value;
        }

        public int CompareTo(GraphicsBufferHandle other)
        {
            return value.CompareTo(other.value);
        }

        public static bool operator ==(GraphicsBufferHandle a, GraphicsBufferHandle b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(GraphicsBufferHandle a, GraphicsBufferHandle b)
        {
            return !a.Equals(b);
        }
    }

    // Note: both C# ComputeBuffer and GraphicsBuffer
    // use C++ GraphicsBuffer as an implementation object.
    [UsedByNativeCode]
    [NativeHeader("Runtime/Shaders/GraphicsBuffer.h")]
    [NativeHeader("Runtime/Export/Graphics/GraphicsBuffer.bindings.h")]
    public sealed class GraphicsBuffer : IDisposable
    {
#pragma warning disable 414
        internal IntPtr m_Ptr;
#pragma warning restore 414

        AtomicSafetyHandle m_Safety;

        [Flags]
        public enum Target
        {
            Vertex            = 1 << 0,
            Index             = 1 << 1,
            CopySource        = 1 << 2,
            CopyDestination   = 1 << 3,
            Structured        = 1 << 4,
            Raw               = 1 << 5,
            Append            = 1 << 6,
            Counter           = 1 << 7,
            IndirectArguments = 1 << 8,
            Constant          = 1 << 9,
        }

        [Flags]
        public enum UsageFlags
        {
            None               = 0,
            LockBufferForWrite = 1 << 0,
        }

        public struct IndirectDrawArgs
        {
            public const int size = 16;
            public uint vertexCountPerInstance {get; set;}
            public uint instanceCount {get; set;}
            public uint startVertex {get; set;}
            public uint startInstance {get; set;}
        }

        public struct IndirectDrawIndexedArgs
        {
            public const int size = 20;
            public uint indexCountPerInstance {get; set;}
            public uint instanceCount {get; set;}
            public uint startIndex {get; set;}
            public uint baseVertexIndex {get; set;}
            public uint startInstance {get; set;}
        }

        ~GraphicsBuffer()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Release native resources
                DestroyBuffer(this);
            }
            else if (m_Ptr != IntPtr.Zero)
            {
                Debug.LogWarning($"GarbageCollector disposing of GraphicsBuffer allocated in {GetFileName()} at line {GetLineNumber()}. Please use GraphicsBuffer.Release() or .Dispose() to manually release the buffer.");
            }

            m_Ptr = IntPtr.Zero;
        }

        static bool RequiresCompute(Target target)
        {
            var requiresComputeMask = Target.Structured | Target.Raw | Target.Append | Target.Counter | Target.IndirectArguments;
            return (target & requiresComputeMask) != 0;
        }

        static bool IsVertexIndexOrCopyOnly(Target target)
        {
            var mask = Target.Vertex | Target.Index | Target.CopySource | Target.CopyDestination;
            return (target & mask) == target;
        }

        [FreeFunction("GraphicsBuffer_Bindings::InitBuffer")]
        static extern IntPtr InitBuffer(Target target, UsageFlags usageFlags, int count, int stride);

        [FreeFunction("GraphicsBuffer_Bindings::DestroyBuffer")]
        static extern void DestroyBuffer(GraphicsBuffer buf);

        // Create a Graphics Buffer.
        public GraphicsBuffer(Target target, int count, int stride)
        {
            // If usage is not explicitly specified, then it defaults to:
            // - Pure vertex or index buffer: "sub-updates",
            // - All other targets: "immutable".
            // It does not make sense, except that at some point C# classes behavior was
            // like that (C# GraphicsBuffer, when it only supported Vertex/Index buffers always used
            // sub-updates; and C# ComputeBuffer, which only suppported non-Vertex/Index always used
            // immutable). And now we can't change this default, ever :/
            bool onlyVBIB = (target & (Target.Index | Target.Vertex)) == target;
            var usageFlags = onlyVBIB ? UsageFlags.LockBufferForWrite : UsageFlags.None;

            InternalInitialization(target, usageFlags, count, stride);

            SaveCallstack(2);
        }

        // Create a Graphics Buffer.
        public GraphicsBuffer(Target target, UsageFlags usageFlags, int count, int stride)
        {
            InternalInitialization(target, usageFlags, count, stride);

            SaveCallstack(2);
        }

        private void InternalInitialization(Target target, UsageFlags usageFlags, int count, int stride)
        {
            if (RequiresCompute(target) && !SystemInfo.supportsComputeShaders)
            {
                throw new ArgumentException("Attempting to create a graphics buffer that requires compute shader support, but compute shaders are not supported on this platform. Target: " + target);
            }

            if (count <= 0)
            {
                throw new ArgumentException("Attempting to create a zero length graphics buffer", "count");
            }

            if (stride <= 0)
            {
                throw new ArgumentException("Attempting to create a graphics buffer with a negative or null stride", "stride");
            }

            if ((target & Target.Index) != 0 && stride != 2 && stride != 4)
            {
                throw new ArgumentException("Attempting to create an index buffer with an invalid stride: " + stride, "stride");
            }
            else if (!IsVertexIndexOrCopyOnly(target) && stride % 4 != 0)
            {
                throw new ArgumentException("Stride must be a multiple of 4 unless the buffer is only used as a vertex buffer and/or index buffer ", "stride");
            }

            var bufferSize = (long)count * stride;
            var maxBufferSize = SystemInfo.maxGraphicsBufferSize;
            if (bufferSize > maxBufferSize)
            {
                throw new ArgumentException($"The total size of the graphics buffer ({bufferSize} bytes) exceeds the maximum buffer size. Maximum supported buffer size: {maxBufferSize} bytes.");
            }

            m_Ptr = InitBuffer(target, usageFlags, count, stride);

        }

        // Release a Graphics Buffer.
        public void Release()
        {
            Dispose();
        }

        [FreeFunction("GraphicsBuffer_Bindings::IsValidBuffer")]
        static extern bool IsValidBuffer(GraphicsBuffer buf);

        public bool IsValid()
        {
            return m_Ptr != IntPtr.Zero && IsValidBuffer(this);
        }

        // Number of elements in the buffer (RO).
        public extern int count { get; }

        // Size of one element in the buffer (RO).
        public extern int stride { get; }

        public extern Target target { get; }

        [FreeFunction(Name = "GraphicsBuffer_Bindings::GetUsageFlags", HasExplicitThis = true)]
        extern UsageFlags GetUsageFlags();

        public UsageFlags usageFlags { get { return GetUsageFlags(); } }

        public extern GraphicsBufferHandle bufferHandle { get; }

        // Set buffer data.
        [System.Security.SecuritySafeCritical] // due to Marshal.SizeOf
        public void SetData(System.Array data)
        {
            if (data == null)
                throw new ArgumentNullException("data");

            if (!UnsafeUtility.IsArrayBlittable(data))
            {
                throw new ArgumentException(
                    string.Format("Array passed to GraphicsBuffer.SetData(array) must be blittable.\n{0}",
                        UnsafeUtility.GetReasonForArrayNonBlittable(data)));
            }

            InternalSetData(data, 0, 0, data.Length, UnsafeUtility.SizeOf(data.GetType().GetElementType()));
        }

        // Set buffer data.
        [System.Security.SecuritySafeCritical] // due to Marshal.SizeOf
        public void SetData<T>(List<T> data) where T : struct
        {
            if (data == null)
                throw new ArgumentNullException("data");

            if (!UnsafeUtility.IsGenericListBlittable<T>())
            {
                throw new ArgumentException(
                    string.Format("List<{0}> passed to GraphicsBuffer.SetData(List<>) must be blittable.\n{1}",
                        typeof(T), UnsafeUtility.GetReasonForGenericListNonBlittable<T>()));
            }

            InternalSetData(NoAllocHelpers.ExtractArrayFromList(data), 0, 0, NoAllocHelpers.SafeLength(data), Marshal.SizeOf(typeof(T)));
        }

        [System.Security.SecuritySafeCritical] // due to Marshal.SizeOf
        unsafe public void SetData<T>(NativeArray<T> data) where T : struct
        {
            // Note: no IsBlittable test here because it's already done at NativeArray creation time
            InternalSetNativeData((IntPtr)data.GetUnsafeReadOnlyPtr(), 0, 0, data.Length, UnsafeUtility.SizeOf<T>());
        }

        // Set partial buffer data
        [System.Security.SecuritySafeCritical] // due to Marshal.SizeOf
        public void SetData(System.Array data, int managedBufferStartIndex, int graphicsBufferStartIndex, int count)
        {
            if (data == null)
                throw new ArgumentNullException("data");

            if (!UnsafeUtility.IsArrayBlittable(data))
            {
                throw new ArgumentException(
                    string.Format("Array passed to GraphicsBuffer.SetData(array) must be blittable.\n{0}",
                        UnsafeUtility.GetReasonForArrayNonBlittable(data)));
            }

            if (managedBufferStartIndex < 0 || graphicsBufferStartIndex < 0 || count < 0 || managedBufferStartIndex + count > data.Length)
                throw new ArgumentOutOfRangeException(String.Format("Bad indices/count arguments (managedBufferStartIndex:{0} graphicsBufferStartIndex:{1} count:{2})", managedBufferStartIndex, graphicsBufferStartIndex, count));

            InternalSetData(data, managedBufferStartIndex, graphicsBufferStartIndex, count, Marshal.SizeOf(data.GetType().GetElementType()));
        }

        // Set partial buffer data
        [System.Security.SecuritySafeCritical] // due to Marshal.SizeOf
        public void SetData<T>(List<T> data, int managedBufferStartIndex, int graphicsBufferStartIndex, int count) where T : struct
        {
            if (data == null)
                throw new ArgumentNullException("data");

            if (!UnsafeUtility.IsGenericListBlittable<T>())
            {
                throw new ArgumentException(
                    string.Format("List<{0}> passed to GraphicsBuffer.SetData(List<>) must be blittable.\n{1}",
                        typeof(T), UnsafeUtility.GetReasonForGenericListNonBlittable<T>()));
            }

            if (managedBufferStartIndex < 0 || graphicsBufferStartIndex < 0 || count < 0 || managedBufferStartIndex + count > data.Count)
                throw new ArgumentOutOfRangeException(String.Format("Bad indices/count arguments (managedBufferStartIndex:{0} graphicsBufferStartIndex:{1} count:{2})", managedBufferStartIndex, graphicsBufferStartIndex, count));

            InternalSetData(NoAllocHelpers.ExtractArrayFromList(data), managedBufferStartIndex, graphicsBufferStartIndex, count, Marshal.SizeOf(typeof(T)));
        }

        [System.Security.SecuritySafeCritical] // due to Marshal.SizeOf
        public unsafe void SetData<T>(NativeArray<T> data, int nativeBufferStartIndex, int graphicsBufferStartIndex, int count) where T : struct
        {
            // Note: no IsBlittable test here because it's already done at NativeArray creation time
            if (nativeBufferStartIndex < 0 || graphicsBufferStartIndex < 0 || count < 0 || nativeBufferStartIndex + count > data.Length)
                throw new ArgumentOutOfRangeException(String.Format("Bad indices/count arguments (nativeBufferStartIndex:{0} graphicsBufferStartIndex:{1} count:{2})", nativeBufferStartIndex, graphicsBufferStartIndex, count));

            InternalSetNativeData((IntPtr)data.GetUnsafeReadOnlyPtr(), nativeBufferStartIndex, graphicsBufferStartIndex, count, UnsafeUtility.SizeOf<T>());
        }

        [System.Security.SecurityCritical] // to prevent accidentally making this public in the future
        [FreeFunction(Name = "GraphicsBuffer_Bindings::InternalSetNativeData", HasExplicitThis = true, ThrowsException = true)]
        extern private void InternalSetNativeData(IntPtr data, int nativeBufferStartIndex, int graphicsBufferStartIndex, int count, int elemSize);

        [System.Security.SecurityCritical] // to prevent accidentally making this public in the future
        [FreeFunction(Name = "GraphicsBuffer_Bindings::InternalSetData", HasExplicitThis = true, ThrowsException = true)]
        extern private void InternalSetData(System.Array data, int managedBufferStartIndex, int graphicsBufferStartIndex, int count, int elemSize);

        [System.Security.SecurityCritical] // due to Marshal.SizeOf
        public void GetData(System.Array data)
        {
            if (data == null)
                throw new ArgumentNullException("data");

            if (!UnsafeUtility.IsArrayBlittable(data))
            {
                throw new ArgumentException(
                    string.Format("Array passed to GraphicsBuffer.GetData(array) must be blittable.\n{0}",
                        UnsafeUtility.GetReasonForArrayNonBlittable(data)));
            }

            InternalGetData(data, 0, 0, data.Length, Marshal.SizeOf(data.GetType().GetElementType()));
        }

        // Read partial buffer data.
        [System.Security.SecurityCritical] // due to Marshal.SizeOf
        public void GetData(System.Array data, int managedBufferStartIndex, int computeBufferStartIndex, int count)
        {
            if (data == null)
                throw new ArgumentNullException("data");

            if (!UnsafeUtility.IsArrayBlittable(data))
            {
                throw new ArgumentException(
                    string.Format("Array passed to GraphicsBuffer.GetData(array) must be blittable.\n{0}",
                        UnsafeUtility.GetReasonForArrayNonBlittable(data)));
            }

            if (managedBufferStartIndex < 0 || computeBufferStartIndex < 0 || count < 0 || managedBufferStartIndex + count > data.Length)
                throw new ArgumentOutOfRangeException(String.Format("Bad indices/count argument (managedBufferStartIndex:{0} computeBufferStartIndex:{1} count:{2})", managedBufferStartIndex, computeBufferStartIndex, count));

            InternalGetData(data, managedBufferStartIndex, computeBufferStartIndex, count, Marshal.SizeOf(data.GetType().GetElementType()));
        }

        [System.Security.SecurityCritical] // to prevent accidentally making this public in the future
        [FreeFunction(Name = "GraphicsBuffer_Bindings::InternalGetData", HasExplicitThis = true, ThrowsException = true)]
        extern private void InternalGetData(System.Array data, int managedBufferStartIndex, int computeBufferStartIndex, int count, int elemSize);

        [FreeFunction(Name = "GraphicsBuffer_Bindings::InternalGetNativeBufferPtr", HasExplicitThis = true)]
        extern public IntPtr GetNativeBufferPtr();

        extern unsafe private void* BeginBufferWrite(int offset = 0, int size = 0);

        public NativeArray<T> LockBufferForWrite<T>(int bufferStartIndex, int count) where T : struct
        {
            if (!IsValid())
                throw new InvalidOperationException("LockBufferForWrite requires a valid GraphicsBuffer");

            if ((usageFlags & UsageFlags.LockBufferForWrite) == 0)
                throw new InvalidOperationException("GraphicsBuffer must be created with usage mode UsageFlage.LockBufferForWrite to use LockBufferForWrite");

            var elementSize = UnsafeUtility.SizeOf<T>();
            if (bufferStartIndex < 0 || count < 0 || (bufferStartIndex + count) * elementSize > this.count * this.stride)
                throw new ArgumentOutOfRangeException(String.Format("Bad indices/count arguments (bufferStartIndex:{0} count:{1} elementSize:{2}, this.count:{3}, this.stride{4})", bufferStartIndex, count, elementSize, this.count, this.stride));

            NativeArray<T> array;
            unsafe
            {
                var ptr = BeginBufferWrite(bufferStartIndex * elementSize, count * elementSize);
                array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>((void*)ptr, count, Allocator.Invalid);
            }
            m_Safety = AtomicSafetyHandle.Create();
            AtomicSafetyHandle.SetAllowSecondaryVersionWriting(m_Safety, true);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, m_Safety);
            return array;
        }

        extern private void EndBufferWrite(int bytesWritten = 0);

        public void UnlockBufferAfterWrite<T>(int countWritten) where T : struct
        {
            try
            {
                AtomicSafetyHandle.CheckExistsAndThrow(m_Safety);
                AtomicSafetyHandle.Release(m_Safety);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException("GraphicsBuffer.UnlockBufferAfterWrite was called without matching GraphicsBuffer.LockBufferForWrite", e);
            }
            if (countWritten < 0)
                throw new ArgumentOutOfRangeException(String.Format("Bad indices/count arguments (countWritten:{0})", countWritten));

            var elementSize = UnsafeUtility.SizeOf<T>();
            EndBufferWrite(countWritten * elementSize);
        }

        public string name { set => SetName(value); }

        [FreeFunction(Name = "GraphicsBuffer_Bindings::SetName", HasExplicitThis = true)]
        extern void SetName(string name);

        // Set counter value of append/consume buffer.
        extern public void SetCounterValue(uint counterValue);

        // Copy counter value of append/consume buffer into another buffer.
        [FreeFunction(Name = "GraphicsBuffer_Bindings::CopyCount")]
        extern private static void CopyCountCC(ComputeBuffer src, ComputeBuffer dst, int dstOffsetBytes);
        [FreeFunction(Name = "GraphicsBuffer_Bindings::CopyCount")]
        extern private static void CopyCountGC(GraphicsBuffer src, ComputeBuffer dst, int dstOffsetBytes);
        [FreeFunction(Name = "GraphicsBuffer_Bindings::CopyCount")]
        extern private static void CopyCountCG(ComputeBuffer src, GraphicsBuffer dst, int dstOffsetBytes);
        [FreeFunction(Name = "GraphicsBuffer_Bindings::CopyCount")]
        extern private static void CopyCountGG(GraphicsBuffer src, GraphicsBuffer dst, int dstOffsetBytes);

        public static void CopyCount(ComputeBuffer src, ComputeBuffer dst, int dstOffsetBytes)
        {
            CopyCountCC(src, dst, dstOffsetBytes);
        }

        public static void CopyCount(GraphicsBuffer src, ComputeBuffer dst, int dstOffsetBytes)
        {
            CopyCountGC(src, dst, dstOffsetBytes);
        }

        public static void CopyCount(ComputeBuffer src, GraphicsBuffer dst, int dstOffsetBytes)
        {
            CopyCountCG(src, dst, dstOffsetBytes);
        }

        public static void CopyCount(GraphicsBuffer src, GraphicsBuffer dst, int dstOffsetBytes)
        {
            CopyCountGG(src, dst, dstOffsetBytes);
        }

        [ThreadSafe] extern string GetFileName();
        [ThreadSafe] extern int GetLineNumber();
        internal void SaveCallstack(int stackDepth)
        {
            var frame = new StackFrame(stackDepth, true);
            SetAllocationData(frame.GetFileName(), frame.GetFileLineNumber());
        }

        extern void SetAllocationData(string fileName, int lineNumber);
    }
}
