﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NHotPhrase.Keyboard
{
    //Based on https://gist.github.com/Stasonix
    public class GlobalKeyboardHook : IDisposable
    {
        public const int WhKeyboardLl = 13;
        public IntPtr User32LibraryHandle;
        public IntPtr WindowsHookHandle;
        public HookProcDelegate HookProc;

        public delegate IntPtr HookProcDelegate(int nCode, IntPtr wParam, IntPtr lParam);

        public event EventHandler<GlobalKeyboardHookEventArgs> KeyboardPressedEvent;

        /// <summary>
        /// </summary>
        /// <param name="registeredKeys">PKey that should trigger logging. Pass null for full logging.</param>
        /// <param name="keyboardPressedEvent"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public GlobalKeyboardHook(EventHandler<GlobalKeyboardHookEventArgs> keyboardPressedEvent)
        {
            // ReSharper disable once JoinNullCheckWithUsage
            if (keyboardPressedEvent == null)
#pragma warning disable IDE0016 // Use 'throw' expression
                throw new ArgumentNullException(nameof(keyboardPressedEvent));
#pragma warning restore IDE0016 // Use 'throw' expression

            WindowsHookHandle = IntPtr.Zero;
            User32LibraryHandle = IntPtr.Zero;
            HookProc = LowLevelKeyboardProc; // we must keep alive _hookProc, because GC is not aware about SetWindowsHookEx behaviour.

            User32LibraryHandle = Win32.LoadLibrary("User32");
            if (User32LibraryHandle == IntPtr.Zero)
            {
                var errorCode = Marshal.GetLastWin32Error();
                throw new Win32Exception(errorCode,
                    $"Failed to load library 'User32.dll'. Error {errorCode}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}.");
            }

            WindowsHookHandle = Win32.SetWindowsHookEx(WhKeyboardLl, HookProc, User32LibraryHandle, 0);
            if (WindowsHookHandle == IntPtr.Zero)
            {
                var errorCode = Marshal.GetLastWin32Error();
                throw new Win32Exception(errorCode,
                    $"Failed to adjust keyboard hooks for '{Process.GetCurrentProcess().ProcessName}'. Error {errorCode}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}.");
            }
            KeyboardPressedEvent = keyboardPressedEvent;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
                // because we can unhook only in the same thread, not in garbage collector thread
                if (WindowsHookHandle != IntPtr.Zero)
                {
                    if (!Win32.UnhookWindowsHookEx(WindowsHookHandle))
                    {
                        var errorCode = Marshal.GetLastWin32Error();
                        throw new Win32Exception(errorCode,
                            $"Failed to remove keyboard hooks for '{Process.GetCurrentProcess().ProcessName}'. Error {errorCode}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}.");
                    }

                    WindowsHookHandle = IntPtr.Zero;

                    // ReSharper disable once DelegateSubtraction
                    HookProc -= LowLevelKeyboardProc;
                }

            if (User32LibraryHandle == IntPtr.Zero) return;
            
            if (!Win32.FreeLibrary(User32LibraryHandle)) // reduces reference to library by 1.
            {
                var errorCode = Marshal.GetLastWin32Error();
                throw new Win32Exception(errorCode,
                    $"Failed to unload library 'User32.dll'. Error {errorCode}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}.");
            }

            User32LibraryHandle = IntPtr.Zero;
        }

        ~GlobalKeyboardHook()
        {
            Dispose(false);
        }

        public IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            var keystrokeWasHandled = false;
            var keyboardStateAsInt = wParam.ToInt32();
            
            if (Enum.IsDefined(typeof(KeyboardState), keyboardStateAsInt))
            {
                var rawLowLevelKeyboardInputEvent = Marshal.PtrToStructure(lParam, typeof(LowLevelKeyboardInputEvent));
                if (rawLowLevelKeyboardInputEvent != null)
                {
                    var lowLevelKeyboardInputEvent = (LowLevelKeyboardInputEvent) rawLowLevelKeyboardInputEvent;
                    var eventArguments = new GlobalKeyboardHookEventArgs(lowLevelKeyboardInputEvent, (KeyboardState) keyboardStateAsInt);
                    keystrokeWasHandled = HandleKeyEvent(lowLevelKeyboardInputEvent, eventArguments);
                }
            }

            return keystrokeWasHandled 
                ? (IntPtr) 1 
                : Win32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        // ReSharper disable once UnusedParameter.Global
#pragma warning disable IDE0060 // Remove unused parameter
        public bool HandleKeyEvent(LowLevelKeyboardInputEvent lowLevelKeyboardInputEvent, GlobalKeyboardHookEventArgs eventArguments)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            if (KeyboardPressedEvent == null)
                return false;

            var handler = KeyboardPressedEvent;
            handler?.Invoke(this, eventArguments);
            return eventArguments.Handled;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}