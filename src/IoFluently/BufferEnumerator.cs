using System;
using System.IO;

namespace IoFluently
{
    public ref struct BufferEnumerator
    {
        private Stream _stream;
        private readonly int _paddingAtStart;
        private readonly int _paddingAtEnd;
        private ulong _streamLength;
        public ulong ByteOffset { get; private set; }
        public int BufferSize { get; set; }
        private readonly IDisposable _disposable;

        public BufferEnumerator(BufferEnumeratorState state) : this(state.Stream, state.ByteOffset, state.BufferSize,
            state.PaddingAtStart, state.PaddingAtEnd)
        {
            
        }
        
        public BufferEnumerator(Stream stream, ulong byteOffsetOfStart, int bufferSize, int paddingAtStart, int paddingAtEnd)
        {
            _stream = stream;
            _paddingAtStart = paddingAtStart;
            _paddingAtEnd = paddingAtEnd;
            _streamLength = _stream.Length > 0 ? (ulong) _stream.Length : ulong.MaxValue;
            ByteOffset = byteOffsetOfStart;
            Current = default;
            BufferSize = bufferSize;
            _disposable = _stream;
            _stream.Seek((long) byteOffsetOfStart, SeekOrigin.Begin);
        }

        public BufferEnumeratorState State => new BufferEnumeratorState()
        {
            Stream = _stream,
            BufferSize = BufferSize,
            ByteOffset = ByteOffset,
            PaddingAtEnd = _paddingAtEnd,
            PaddingAtStart = _paddingAtStart
        };

        // Needed to be compatible with the foreach operator
        public BufferEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            var unpaddedBufferLength = Math.Min(BufferSize, (int)_streamLength);
            var buffer = new byte[_paddingAtStart + unpaddedBufferLength + _paddingAtEnd];
            var length = _stream.Read(buffer, _paddingAtStart, unpaddedBufferLength);
            if (length <= 0)
            {
                Current = default;
                return false;
            }
            
            Current = new Buffer(buffer.AsSpan(0, _paddingAtStart + length + _paddingAtEnd), ByteOffset, _paddingAtStart, _paddingAtEnd);
            ByteOffset += (ulong) length;
            return true;
        }

        public Buffer Current { get; private set; }

        public void Dispose()
        {
            _disposable?.Dispose();
        }
    }
}