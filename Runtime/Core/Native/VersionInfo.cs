/// Copyright (c)  2025  Xiaomi Corporation (authors: Fangjun Kuang)
using System;
using System.Runtime.InteropServices;
using System.Text;


namespace Eitan.SherpaONNXUnity.Runtime.Native
{
    public class VersionInfo
    {
        public static String Version
        {
          get
          {
            IntPtr p = SherpaOnnxGetVersionStr();

            string s = "";
            int length = 0;

            unsafe
            {
                byte* b = (byte*)p;
                if (b != null)
                {
                    while (*b != 0)
                    {
                        ++b;
                        length += 1;
                    }
                }
            }

            if (length > 0)
            {
                byte[] stringBuffer = new byte[length];
                Marshal.Copy(p, stringBuffer, 0, length);
                s = Encoding.UTF8.GetString(stringBuffer);
            }

            return s;
          }
        }

        public static String GitSha1
        {
          get
          {
            IntPtr p = SherpaOnnxGetGitSha1();

            string s = "";
            int length = 0;

            unsafe
            {
                byte* b = (byte*)p;
                if (b != null)
                {
                    while (*b != 0)
                    {
                        ++b;
                        length += 1;
                    }
                }
            }

            if (length > 0)
            {
                byte[] stringBuffer = new byte[length];
                Marshal.Copy(p, stringBuffer, 0, length);
                s = Encoding.UTF8.GetString(stringBuffer);
            }

            return s;
          }
        }

        public static String GitDate
        {
          get
          {
            IntPtr p = SherpaOnnxGetGitDate();

            string s = "";
            int length = 0;

            unsafe
            {
                byte* b = (byte*)p;
                if (b != null)
                {
                    while (*b != 0)
                    {
                        ++b;
                        length += 1;
                    }
                }
            }

            if (length > 0)
            {
                byte[] stringBuffer = new byte[length];
                Marshal.Copy(p, stringBuffer, 0, length);
                s = Encoding.UTF8.GetString(stringBuffer);
            }

            return s;
          }
        }

        public static String OnnxRuntimeVersion
        {
          get
          {
            IntPtr p;
            try
            {
                p = SherpaOnnxGetOnnxruntimeVersionStr();
            }
            catch (EntryPointNotFoundException)
            {
                // Older non-Windows native packages may not expose this diagnostic yet.
                return String.Empty;
            }
            if (p == IntPtr.Zero)
            {
                return String.Empty;
            }

            int length = 0;
            unsafe
            {
                byte* b = (byte*)p;
                while (*b != 0)
                {
                    ++b;
                    length += 1;
                }
            }

            if (length == 0)
            {
                return String.Empty;
            }

            byte[] stringBuffer = new byte[length];
            Marshal.Copy(p, stringBuffer, 0, length);
            return Encoding.UTF8.GetString(stringBuffer);
          }
        }


        [DllImport(Dll.Filename)]
        private static extern IntPtr SherpaOnnxGetVersionStr();

        [DllImport(Dll.Filename)]
        private static extern IntPtr SherpaOnnxGetGitSha1();

        [DllImport(Dll.Filename)]
        private static extern IntPtr SherpaOnnxGetGitDate();

        [DllImport(Dll.Filename, EntryPoint = "SherpaOnnxGetOnnxruntimeVersionStr")]
        private static extern IntPtr SherpaOnnxGetOnnxruntimeVersionStr();
    }
}
