﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Net.Sockets;
using System.IO;
using System.Text;
using System.Diagnostics;
using Squared.Util;

namespace Squared.Task.IO {
    public class SocketDisconnectedException : IOException {
        public SocketDisconnectedException ()
            : base("The operation failed because the socket has disconnected.") {
        }
    }

    public class SocketBufferFullException : IOException {
        public SocketBufferFullException ()
            : base("The operation failed because the socket's buffer is full.") {
        }
    }

    public class SocketDataAdapter : IAsyncDataSource, IAsyncDataWriter {
        public bool ThrowOnDisconnect = true, ThrowOnFullSendBuffer = true;
        Socket _Socket;
        readonly bool _OwnsSocket;
        readonly AsyncCallback _ReadCallback, _WriteCallback;

        public SocketDataAdapter (Socket socket, bool ownsSocket = true) {
            _Socket = socket;
            _OwnsSocket = ownsSocket;
            _ReadCallback = ReadCallback;
            _WriteCallback = WriteCallback;
        }

        public void Dispose () {
            if (_OwnsSocket && (_Socket != null)) {
                _Socket.Shutdown(SocketShutdown.Both);
                _Socket.Close();
                _Socket = null;
            }
        }

        private void ReadCallback (IAsyncResult ar) {
            var f = (Future<int>)ar.AsyncState;

            if (_Socket == null || !_Socket.Connected) {
                if (ThrowOnDisconnect)
                    f.Fail(new SocketDisconnectedException());
                else
                    f.Complete(0);

                return;
            }

            try {
                int bytesRead = _Socket.EndReceive(ar);
                if (ThrowOnDisconnect && (bytesRead == 0)) {
                    f.Fail(new SocketDisconnectedException());
                } else {
                    f.Complete(bytesRead);
                }
            } catch (FutureHandlerException) {
                throw;
            } catch (Exception ex) {
                f.Fail(ex);
            }
        }

        public Future<int> Read (byte[] buffer, int offset, int count) {
            var f = new Future<int>();
            if (!_Socket.Connected) {
                if (ThrowOnDisconnect)
                    f.Fail(new SocketDisconnectedException());
                else
                    f.Complete(0);
            } else {
                SocketError errorCode;
                if (_Socket.Available >= count) {
                    try {
                        int bytesRead = _Socket.Receive(buffer, offset, count, SocketFlags.None, out errorCode);
                        if (ThrowOnDisconnect && (bytesRead == 0)) {
                            f.Fail(new SocketDisconnectedException());
                        } else {
                            f.Complete(bytesRead);
                        }
                    } catch (Exception ex) {
                        f.Fail(ex);
                    }
                } else {
                    _Socket.BeginReceive(buffer, offset, count, SocketFlags.None, out errorCode, _ReadCallback, f);
                }
            }
            return f;
        }

        private bool IsSendBufferFull () {
            if (_Socket.Blocking)
                return false;

            return !_Socket.Poll(0, SelectMode.SelectWrite);
        }

        private void WriteCallback (IAsyncResult ar) {
            var f = (SignalFuture)ar.AsyncState;

            if (!_Socket.Connected) {
                if (ThrowOnDisconnect)
                    f.Fail(new SocketDisconnectedException());
                else
                    f.Complete();

                return;
            }

            try {
                _Socket.EndSend(ar);
                f.Complete();
            } catch (FutureHandlerException) {
                throw;
            } catch (Exception ex) {
                f.Fail(ex);
            }
        }

        public SignalFuture Write (byte[] buffer, int offset, int count) {
            var f = new SignalFuture();
            if (!_Socket.Connected) {
                if (ThrowOnDisconnect)
                    f.Fail(new SocketDisconnectedException());
                else
                    f.Complete();

            } else {
                if (ThrowOnFullSendBuffer && IsSendBufferFull())
                    throw new SocketBufferFullException();

                SocketError errorCode;
                _Socket.BeginSend(buffer, offset, count, SocketFlags.None, out errorCode, _WriteCallback, f);
            }
            return f;
        }

        public bool EndOfStream {
            get {
                return !_Socket.Connected;
            }
        }
    }
}
