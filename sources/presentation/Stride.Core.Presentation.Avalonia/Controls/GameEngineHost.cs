// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Stride.Core.Presentation.Avalonia.Controls;

/// <summary>
/// A <see cref="NativeControlHost"/> that can host a game engine window.
/// </summary>
public class GameEngineHost : NativeControlHost, IDisposable
{
    private bool isDisposed;

    static GameEngineHost()
    {
        FocusableProperty.OverrideMetadata(typeof(GameEngineHost), new StyledPropertyMetadata<bool>(true));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GameEngineHost"/> class.
    /// </summary>
    /// <param name="childHandle">The native handle of the child (hosted) window.</param>
    public GameEngineHost(IntPtr childHandle)
    {
        Handle = childHandle;
        MinWidth = 32;
        MinHeight = 32;
    }

    public IntPtr Handle { get; }

    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;
    }

    /// <inheritdoc />
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        return Handle != IntPtr.Zero
            ? new PlatformHandle(Handle, parent.HandleDescriptor)
            : base.CreateNativeControlCore(parent);
    }

    /// <inheritdoc />
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (Handle == IntPtr.Zero)
        {
            base.DestroyNativeControlCore(control);
        }
    }

    /// <summary>
    /// Forwards a message that comes from the hosted window to the WPF window. This method can be used for example to forward keyboard events.
    /// </summary>
    /// <param name="hwnd">The hwnd of the hosted window.</param>
    /// <param name="msg">The message identifier.</param>
    /// <param name="wParam">The word parameter of the message.</param>
    /// <param name="lParam">The long parameter of the message.</param>
    public void ForwardMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        // FIXME xplat-editor
        //switch (msg)
        //{
        //    case NativeHelper.WM_RBUTTONDOWN:
        //        mouseMoveCount = 0;
        //        task = Dispatcher.InvokeAsync(() =>
        //        {
        //            RaiseMouseButtonEvent(Mouse.PreviewMouseDownEvent, MouseButton.Right);
        //            RaiseMouseButtonEvent(Mouse.MouseDownEvent, MouseButton.Right);
        //        });
        //        task.Wait(TimeSpan.FromSeconds(1.0f));
        //        break;
        //    case NativeHelper.WM_RBUTTONUP:
        //        task = Dispatcher.InvokeAsync(() =>
        //        {
        //            RaiseMouseButtonEvent(Mouse.PreviewMouseUpEvent, MouseButton.Right);
        //            RaiseMouseButtonEvent(Mouse.MouseUpEvent, MouseButton.Right);
        //        });
        //        task.Wait(TimeSpan.FromSeconds(1.0f));
        //        break;
        //    case NativeHelper.WM_LBUTTONDOWN:
        //        task = Dispatcher.InvokeAsync(() =>
        //        {
        //            RaiseMouseButtonEvent(Mouse.PreviewMouseDownEvent, MouseButton.Left);
        //            RaiseMouseButtonEvent(Mouse.MouseDownEvent, MouseButton.Left);
        //        });
        //        task.Wait(TimeSpan.FromSeconds(1.0f));
        //        break;
        //    case NativeHelper.WM_LBUTTONUP:
        //        task = Dispatcher.InvokeAsync(() =>
        //        {
        //            RaiseMouseButtonEvent(Mouse.PreviewMouseUpEvent, MouseButton.Left);
        //            RaiseMouseButtonEvent(Mouse.MouseUpEvent, MouseButton.Left);
        //        });
        //        task.Wait(TimeSpan.FromSeconds(1.0f));
        //        break;
        //    case NativeHelper.WM_MOUSEMOVE:
        //        ++mouseMoveCount;
        //        break;
        //    case NativeHelper.WM_CONTEXTMENU:
        //        // TODO: Tracking drag offset would be better, but might be difficult since we replace the mouse to its initial position each time it is moved.
        //        if (mouseMoveCount < 3)
        //        {
        //            Dispatcher.InvokeAsync(() =>
        //            {
        //                Visual? dependencyObject = this;
        //                while (dependencyObject != null)
        //                {
        //                    var element = dependencyObject as Control;
        //                    if (element?.ContextMenu != null)
        //                    {
        //                        element.Focus();
        //                        // Data context will not be properly set if the popup is open this way, so let's set it ourselves
        //                        element.ContextMenu.SetCurrentValue(DataContextProperty, element.DataContext);
        //                        element.ContextMenu.IsOpen = true;
        //                        var source = (HwndSource)PresentationSource.FromVisual(element.ContextMenu);
        //                        if (source != null)
        //                        {
        //                            source.AddHook(ContextMenuWndProc);
        //                            contextMenuPosition = Mouse.GetPosition(this);
        //                            lock (contextMenuSources)
        //                            {
        //                                contextMenuSources.Add(source);
        //                            }
        //                        }
        //                        break;
        //                    }
        //                    dependencyObject = dependencyObject.GetVisualParent();
        //                }
        //            });
        //        }
        //        break;
        //    default:
        //        var parent = NativeHelper.GetParent(hwnd);
        //        NativeHelper.PostMessage(parent, msg, wParam, lParam);
        //        break;
        //}
    }

    //private void RaiseMouseButtonEvent(RoutedEvent routedEvent, MouseButton button)
    //{
    //    RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button)
    //    {
    //        RoutedEvent = routedEvent,
    //        Source = this,
    //    });
    //}

    //private IntPtr ContextMenuWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    //{
    //    switch (msg)
    //    {
    //        case NativeHelper.WM_LBUTTONDOWN:
    //        case NativeHelper.WM_RBUTTONDOWN:
    //            // We need to change from the context menu coordinates to the HwndHost coordinates and re-encode lParam
    //            var position = new Point(-(short)(lParam.ToInt64() & 0xFFFF), -((lParam.ToInt64() & 0xFFFF0000) >> 16));
    //            var offset = contextMenuPosition - position;
    //            lParam = new IntPtr((short)offset.X + ((short)offset.Y << 16));
    //            var threadId = NativeHelper.GetWindowThreadProcessId(Handle, IntPtr.Zero);
    //            NativeHelper.PostThreadMessage(threadId, msg, wParam, lParam);
    //            break;
    //        case NativeHelper.WM_DESTROY:
    //            lock (contextMenuSources)
    //            {
    //                var source = contextMenuSources.First(x => x.Handle == hwnd);
    //                source.RemoveHook(ContextMenuWndProc);
    //            }
    //            break;
    //    }
    //    return IntPtr.Zero;
    //}
    
    //[CanBeNull]
    //[MethodImpl(MethodImplOptions.AggressiveInlining)]
    //private HwndSource GetHwndSource()
    //{
    //    return (HwndSource)PresentationSource.FromVisual(this);
    //}
    
    // FIXME xplat-editor
    //IKeyboardInputSite IKeyboardInputSink.RegisterKeyboardInputSink(IKeyboardInputSink sink)
    //{
    //    throw new NotSupportedException();
    //}

    //bool IKeyboardInputSink.TranslateAccelerator(ref MSG msg, ModifierKeys modifiers)
    //{
    //    return false;
    //}

    //bool IKeyboardInputSink.TabInto(TraversalRequest request)
    //{
    //    return false;
    //}

    //bool IKeyboardInputSink.OnMnemonic(ref MSG msg, ModifierKeys modifiers)
    //{
    //    return false;
    //}

    //bool IKeyboardInputSink.TranslateChar(ref MSG msg, ModifierKeys modifiers)
    //{
    //    return false;
    //}

    //bool IKeyboardInputSink.HasFocusWithin()
    //{
    //    var focus = NativeHelper.GetFocus();
    //    return Handle != IntPtr.Zero && (focus == Handle || NativeHelper.IsChild(Handle, focus));
    //}
}
