#region License
/*
 * WsStream.cs
 *
 * The MIT License
 *
 * Copyright (c) 2010-2013 sta.blockhead
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using WebSocketSharp.Net;
using WebSocketSharp.Net.Security;

namespace WebSocketSharp
{
  internal class WsStream : IDisposable
  {
    #region Private Const Fields

    private const int _handshakeLimitLen = 8192;

    #endregion

    #region Private Fields

    private Object _forRead;
    private Object _forWrite;
    private Stream _innerStream;
    private bool   _secure;

    #endregion

    #region Private Constructors

    private WsStream (Stream innerStream, bool secure)
    {
      _innerStream = innerStream;
      _secure = secure;
      _forRead = new object ();
      _forWrite = new object ();
    }

    #endregion

    #region Internal Constructors

    internal WsStream (NetworkStream innerStream)
      : this (innerStream, false)
    {
    }

    internal WsStream (SslStream innerStream)
      : this (innerStream, true)
    {
    }

    #endregion

    #region Public Properties

    public bool DataAvailable {
      get {
        return _secure
               ? ((SslStream) _innerStream).DataAvailable
               : ((NetworkStream) _innerStream).DataAvailable;
      }
    }

    public bool IsSecure {
      get {
        return _secure;
      }
    }

    #endregion

    #region Internal Methods

    internal static WsStream CreateClientStream (
      TcpClient client,
      bool secure,
      string host,
      System.Net.Security.RemoteCertificateValidationCallback validationCallback
    )
    {
      var netStream = client.GetStream ();
      if (secure)
      {
        if (validationCallback == null)
          validationCallback = (sender, certificate, chain, sslPolicyErrors) => true;

        var sslStream = new SslStream (netStream, false, validationCallback);
        sslStream.AuthenticateAsClient (host);

        return new WsStream (sslStream);
      }

      return new WsStream (netStream);
    }

    internal static WsStream CreateServerStream (TcpClient client, bool secure, X509Certificate cert)
    {
      var netStream = client.GetStream ();
      if (secure)
      {
        var sslStream = new SslStream (netStream, false);
        sslStream.AuthenticateAsServer (cert);

        return new WsStream (sslStream);
      }

      return new WsStream (netStream);
    }

    internal static WsStream CreateServerStream (HttpListenerContext context)
    {
      var conn = context.Connection;
      return new WsStream (conn.Stream, conn.IsSecure);
    }

    internal bool Write (byte [] data)
    {
      lock (_forWrite)
      {
        try {
          _innerStream.Write (data, 0, data.Length);
          return true;
        }
        catch {
          return false;
        }
      }
    }

    #endregion

    #region Public Methods

    public void Close ()
    {
      _innerStream.Close ();
    }

    public void Dispose ()
    {
      _innerStream.Dispose ();
    }

    public WsFrame ReadFrame ()
    {
      lock (_forRead)
      {
        try {
          return WsFrame.Parse (_innerStream);
        }
        catch {
          return null;
        }
      }
    }

    public void ReadFrameAsync (Action<WsFrame> completed)
    {
      WsFrame.ParseAsync (_innerStream, completed);
    }

    public string [] ReadHandshake ()
    {
      var read = false;
      var buffer = new List<byte> ();
      Action<int> add = i => buffer.Add ((byte) i);
      while (buffer.Count < _handshakeLimitLen)
      {
        if (_innerStream.ReadByte ().EqualsWith ('\r', add) &&
            _innerStream.ReadByte ().EqualsWith ('\n', add) &&
            _innerStream.ReadByte ().EqualsWith ('\r', add) &&
            _innerStream.ReadByte ().EqualsWith ('\n', add))
        {
          read = true;
          break;
        }
      }

      if (!read)
        throw new WebSocketException ("The length of the handshake is greater than the limit length.");

      return Encoding.UTF8.GetString (buffer.ToArray ())
             .Replace ("\r\n", "\n")
             .Replace ("\n ", " ")
             .Replace ("\n\t", " ")
             .TrimEnd ('\n')
             .Split ('\n');
    }

    public bool WriteFrame (WsFrame frame)
    {
      return Write (frame.ToByteArray ());
    }

    public bool WriteHandshake (HandshakeBase handshake)
    {
      return Write (handshake.ToByteArray ());
    }

    #endregion
  }
}
