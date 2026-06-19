// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// 
// PROPRIETARY & TRADE SECRET NOTICE:
// This source code and associated documentation (including the HCEP
// Theory, the engineering implementation, the supported mathematical
// formulations, the Permanent Active Directives (PAD), and the Body
// Language Protocols) contain proprietary and trade secret assets
// owned exclusively by Kirk LaSalle. Unauthorized use, copying,
// modification, or distribution is strictly prohibited.
// ──────────────────────────────────────────────────────────────

using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using Microsoft.Kinect;

namespace HCEP.Kinect.Bridge
{
    internal static class Program
    {
        const byte FRAME_COLOR = 0x01;
        const byte FRAME_DEPTH = 0x02;
        const byte FRAME_IR = 0x03;
        const byte FRAME_READY = 0xFE;
        const byte FRAME_ERROR = 0xFF;
        const string PIPE_NAME = "HCEP_KINECT_BRIDGE";

        static KinectSensor? _kinect;
        static BinaryWriter? _writer;
        static readonly object _writeLock = new object();
        static byte[]? _colorPixels;
        static short[]? _rawDepthPixels;
        static int _colorFrameNum;
        static int _depthFrameNum;
        static volatile bool _running = true;

        static void Main(string[] args)
        {
            Console.Error.WriteLine("[Bridge] HCEP Kinect Bridge starting - PID " +
                System.Diagnostics.Process.GetCurrentProcess().Id);
            try
            {
                _kinect = FindKinect();
                if (_kinect == null)
                {
                    Console.Error.WriteLine("[Bridge] ERROR: No connected Kinect sensor found");
                    Environment.Exit(1);
                    return;
                }
                Console.Error.WriteLine("[Bridge] Found Kinect: " + _kinect.UniqueKinectId);

                _kinect.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);
                _kinect.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
                _kinect.SkeletonStream.Enable();
                Console.Error.WriteLine("[Bridge] Streams enabled: Color 640x480@30, Depth 640x480@30, Skeleton");

                _kinect.ColorFrameReady += OnColorFrameReady;
                _kinect.DepthFrameReady += OnDepthFrameReady;
                _kinect.Start();
                Console.Error.WriteLine("[Bridge] Kinect sensor started");

                using (var pipe = new NamedPipeServerStream(PIPE_NAME, PipeDirection.Out, 1,
                    PipeTransmissionMode.Byte, PipeOptions.WriteThrough))
                {
                    Console.Error.WriteLine("[Bridge] Waiting for host on pipe: " + PIPE_NAME);
                    pipe.WaitForConnection();
                    Console.Error.WriteLine("[Bridge] Host connected");
                    _writer = new BinaryWriter(pipe);
                    WriteFrame(FRAME_READY, new byte[0]);
                    Console.Error.WriteLine("[Bridge] READY signal sent - streaming frames");

                    while (_running && pipe.IsConnected)
                        Thread.Sleep(100);
                }
            }
            catch (Exception ex) { Console.Error.WriteLine("[Bridge] FATAL: " + ex); }
            finally { Cleanup(); }
            Console.Error.WriteLine("[Bridge] Exiting");
        }

        static KinectSensor? FindKinect()
        {
            foreach (var sensor in KinectSensor.KinectSensors)
                if (sensor.Status == KinectStatus.Connected) return sensor;
            return null;
        }

        static void OnColorFrameReady(object sender, ColorImageFrameReadyEventArgs e)
        {
            using (var frame = e.OpenColorImageFrame())
            {
                if (frame == null) return;
                int byteCount = frame.PixelDataLength;
                if (_colorPixels == null || _colorPixels.Length != byteCount)
                    _colorPixels = new byte[byteCount];
                frame.CopyPixelDataTo(_colorPixels);
                int frameNum = Interlocked.Increment(ref _colorFrameNum);

                int hdr = 16;
                var payload = new byte[hdr + byteCount];
                WI(payload, 0, frame.Width);
                WI(payload, 4, frame.Height);
                WI(payload, 8, frame.BytesPerPixel);
                WI(payload, 12, frameNum);
                Buffer.BlockCopy(_colorPixels, 0, payload, hdr, byteCount);
                WriteFrame(FRAME_COLOR, payload);

                if (frameNum <= 3)
                    Console.Error.WriteLine(string.Format("[Bridge] Color #{0}: {1}x{2} {3}bpp",
                        frameNum, frame.Width, frame.Height, frame.BytesPerPixel));
            }
        }

        static void OnDepthFrameReady(object sender, DepthImageFrameReadyEventArgs e)
        {
            using (var frame = e.OpenDepthImageFrame())
            {
                if (frame == null) return;
                int pc = frame.PixelDataLength;
                if (_rawDepthPixels == null || _rawDepthPixels.Length != pc)
                    _rawDepthPixels = new short[pc];
                frame.CopyPixelDataTo(_rawDepthPixels);
                int fn = Interlocked.Increment(ref _depthFrameNum);
                int w = frame.Width, h = frame.Height;
                int minD = frame.MinDepth, maxD = frame.MaxDepth;

                var depthMm = new short[pc];
                for (int i = 0; i < pc; i++)
                    depthMm[i] = (short)(_rawDepthPixels[i] >> DepthImageFrame.PlayerIndexBitmaskWidth);

                int db = pc * 2;
                var dp = new byte[20 + db];
                WI(dp, 0, w); WI(dp, 4, h); WI(dp, 8, minD); WI(dp, 12, maxD); WI(dp, 16, fn);
                Buffer.BlockCopy(depthMm, 0, dp, 20, db);
                WriteFrame(FRAME_DEPTH, dp);

                var irPx = new byte[w * h * 4];
                float range = Math.Max(1f, maxD - minD);
                for (int i = 0; i < pc; i++)
                {
                    short d = depthMm[i];
                    byte v;
                    if (d <= 0 || d < minD) v = 10;
                    else if (d > maxD) v = 5;
                    else v = (byte)(255 - (int)((d - minD) / range * 230));
                    int j = i * 4;
                    irPx[j] = v; irPx[j + 1] = v; irPx[j + 2] = v; irPx[j + 3] = 255;
                }
                var ip = new byte[16 + irPx.Length];
                WI(ip, 0, w); WI(ip, 4, h); WI(ip, 8, 4); WI(ip, 12, fn);
                Buffer.BlockCopy(irPx, 0, ip, 16, irPx.Length);
                WriteFrame(FRAME_IR, ip);

                if (fn <= 3)
                    Console.Error.WriteLine(string.Format("[Bridge] Depth #{0}: {1}x{2} min={3} max={4}",
                        fn, w, h, minD, maxD));
            }
        }

        static void WriteFrame(byte ft, byte[] payload)
        {
            lock (_writeLock)
            {
                try
                {
                    if (_writer == null) return;
                    _writer.Write(ft);
                    _writer.Write(payload.Length);
                    if (payload.Length > 0) _writer.Write(payload);
                    _writer.Flush();
                }
                catch (IOException) { _running = false; }
                catch (ObjectDisposedException) { _running = false; }
            }
        }

        static void WI(byte[] b, int o, int v)
        {
            b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24);
        }

        static void Cleanup()
        {
            _running = false;
            try
            {
                if (_kinect != null)
                {
                    _kinect.ColorFrameReady -= OnColorFrameReady;
                    _kinect.DepthFrameReady -= OnDepthFrameReady;
                    _kinect.Stop();
                    _kinect.Dispose();
                    Console.Error.WriteLine("[Bridge] Kinect stopped");
                }
            }
            catch (Exception ex) { Console.Error.WriteLine("[Bridge] Cleanup error: " + ex.Message); }
        }
    }
}