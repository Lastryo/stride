// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;

namespace Stride.Core.Presentation.Avalonia.Behaviors;

/// <summary>
/// A behavior for <see cref="TextBox"/> that commits or cancels changes on <see cref="Key.Enter"/>, <see cref="Key.Escape"/>, and focus loss,
/// and clears focus/caret when <see cref="Key.Enter"/> or <see cref="Key.Escape"/> is pressed.
/// </summary>
public sealed class TextBoxValidationBehavior : StyledElementBehavior<TextBox>
{
    private string? initialText;
    private bool hasPendingChanges;

    public static readonly StyledProperty<ICommand?> ValidateCommandProperty =
        AvaloniaProperty.Register<TextBoxValidationBehavior, ICommand?>(nameof(ValidateCommand));

    public static readonly StyledProperty<object?> ValidateCommandParameterProperty =
        AvaloniaProperty.Register<TextBoxValidationBehavior, object?>(nameof(ValidateCommandParameter));

    public static readonly StyledProperty<ICommand?> CancelCommandProperty =
        AvaloniaProperty.Register<TextBoxValidationBehavior, ICommand?>(nameof(CancelCommand));

    public static readonly StyledProperty<object?> CancelCommandParameterProperty =
        AvaloniaProperty.Register<TextBoxValidationBehavior, object?>(nameof(CancelCommandParameter));

    public static readonly StyledProperty<bool> ValidateWithEnterProperty =
        AvaloniaProperty.Register<TextBoxValidationBehavior, bool>(nameof(ValidateWithEnter), true);

    public static readonly StyledProperty<bool> CancelWithEscapeProperty =
        AvaloniaProperty.Register<TextBoxValidationBehavior, bool>(nameof(CancelWithEscape), true);

    public static readonly StyledProperty<bool> ValidateOnLostFocusProperty =
        AvaloniaProperty.Register<TextBoxValidationBehavior, bool>(nameof(ValidateOnLostFocus), true);

    public static readonly StyledProperty<bool> CancelOnLostFocusProperty =
        AvaloniaProperty.Register<TextBoxValidationBehavior, bool>(nameof(CancelOnLostFocus), false);

    public static readonly StyledProperty<bool> SelectAllOnFocusProperty =
        AvaloniaProperty.Register<TextBoxValidationBehavior, bool>(nameof(SelectAllOnFocus), true);

    public static readonly StyledProperty<bool> ClearFocusOnEnterProperty =
        AvaloniaProperty.Register<TextBoxValidationBehavior, bool>(nameof(ClearFocusOnEnter), true);

    public static readonly StyledProperty<bool> ClosePopupOnValidateOrCancelProperty =
        AvaloniaProperty.Register<TextBoxValidationBehavior, bool>(nameof(ClosePopupOnValidateOrCancel), false);

    public ICommand? ValidateCommand
    {
        get => GetValue(ValidateCommandProperty);
        set => SetValue(ValidateCommandProperty, value);
    }

    public object? ValidateCommandParameter
    {
        get => GetValue(ValidateCommandParameterProperty);
        set => SetValue(ValidateCommandParameterProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public object? CancelCommandParameter
    {
        get => GetValue(CancelCommandParameterProperty);
        set => SetValue(CancelCommandParameterProperty, value);
    }

    public bool ValidateWithEnter
    {
        get => GetValue(ValidateWithEnterProperty);
        set => SetValue(ValidateWithEnterProperty, value);
    }

    public bool CancelWithEscape
    {
        get => GetValue(CancelWithEscapeProperty);
        set => SetValue(CancelWithEscapeProperty, value);
    }

    public bool ValidateOnLostFocus
    {
        get => GetValue(ValidateOnLostFocusProperty);
        set => SetValue(ValidateOnLostFocusProperty, value);
    }

    public bool CancelOnLostFocus
    {
        get => GetValue(CancelOnLostFocusProperty);
        set => SetValue(CancelOnLostFocusProperty, value);
    }

    public bool SelectAllOnFocus
    {
        get => GetValue(SelectAllOnFocusProperty);
        set => SetValue(SelectAllOnFocusProperty, value);
    }

    public bool ClearFocusOnEnter
    {
        get => GetValue(ClearFocusOnEnterProperty);
        set => SetValue(ClearFocusOnEnterProperty, value);
    }

    public bool ClosePopupOnValidateOrCancel
    {
        get => GetValue(ClosePopupOnValidateOrCancelProperty);
        set => SetValue(ClosePopupOnValidateOrCancelProperty, value);
    }

    private Popup? ancestorPopup;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is { } textBox)
        {
            textBox.GotFocus += OnGotFocus;
            textBox.LostFocus += OnLostFocus;
            textBox.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            textBox.AttachedToVisualTree += OnAttachedToVisualTree;
            AttachToPopupIfNeeded(textBox);
        }
    }

    protected override void OnDetaching()
    {
        if (ancestorPopup is not null)
        {
            ancestorPopup.Opened -= OnPopupOpened;
            ancestorPopup = null;
        }
        if (AssociatedObject is { } textBox)
        {
            textBox.GotFocus -= OnGotFocus;
            textBox.LostFocus -= OnLostFocus;
            textBox.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            textBox.AttachedToVisualTree -= OnAttachedToVisualTree;
        }
        base.OnDetaching();
    }

    private void AttachToPopupIfNeeded(TextBox textBox)
    {
        if (!ClosePopupOnValidateOrCancel || ancestorPopup is not null)
            return;

        ancestorPopup = FindAncestorPopup(textBox);
        if (ancestorPopup is not null)
        {
            ancestorPopup.Opened += OnPopupOpened;
        }
    }

    private void OnPopupOpened(object? sender, EventArgs e)
    {
        FocusAndSelectTextBox();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (AssociatedObject is { } textBox)
        {
            AttachToPopupIfNeeded(textBox);
            if (ClosePopupOnValidateOrCancel)
            {
                FocusAndSelectTextBox();
            }
        }
    }

    private void FocusAndSelectTextBox()
    {
        if (AssociatedObject is not { } textBox)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (textBox.IsVisible)
            {
                initialText = textBox.Text;
                hasPendingChanges = true;
                textBox.Focus();
                if (SelectAllOnFocus)
                {
                    textBox.SelectAll();
                }
            }
        }, DispatcherPriority.Input);
    }

    private void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (AssociatedObject is not { } textBox)
            return;

        initialText = textBox.Text;
        hasPendingChanges = true;

        if (SelectAllOnFocus)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (textBox.IsFocused)
                {
                    textBox.SelectAll();
                }
            }, DispatcherPriority.Input);
        }
    }

    private void OnLostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (!hasPendingChanges)
            return;

        if (CancelOnLostFocus)
        {
            Cancel(fromEscape: false);
        }
        else if (ValidateOnLostFocus)
        {
            Validate(fromEnter: false);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ValidateWithEnter)
        {
            Validate(fromEnter: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && CancelWithEscape)
        {
            Cancel(fromEscape: true);
            e.Handled = true;
        }
    }

    private void Validate(bool fromEnter)
    {
        if (AssociatedObject is not { } textBox)
            return;

        hasPendingChanges = false;
        var textToValidate = textBox.Text;

        // TextBox normalizes null to empty; merely focusing a nullable string must not edit it.
        if (!string.Equals(initialText ?? string.Empty, textToValidate ?? string.Empty, StringComparison.Ordinal))
            BindingOperations.GetBindingExpressionBase(textBox, TextBox.TextProperty)?.UpdateSource();

        if (ValidateCommand is { } command)
        {
            var parameter = ValidateCommandParameter ?? textToValidate;
            if (command.CanExecute(parameter))
            {
                command.Execute(parameter);
            }
        }

        initialText = textBox.Text;

        if (ClosePopupOnValidateOrCancel)
        {
            CloseAncestorPopup(textBox);
        }

        if (fromEnter && ClearFocusOnEnter)
        {
            ClearTextBoxFocus(textBox);
        }
    }

    private void Cancel(bool fromEscape)
    {
        if (AssociatedObject is not { } textBox)
            return;

        hasPendingChanges = false;

        if (initialText is not null && !Equals(textBox.Text, initialText))
        {
            textBox.SetCurrentValue(TextBox.TextProperty, initialText);
        }

        if (CancelCommand is { } command && command.CanExecute(CancelCommandParameter))
        {
            command.Execute(CancelCommandParameter);
        }

        if (ClosePopupOnValidateOrCancel)
        {
            CloseAncestorPopup(textBox);
        }

        if (fromEscape)
        {
            ClearTextBoxFocus(textBox);
        }
    }

    private static void ClearTextBoxFocus(TextBox textBox)
    {
        textBox.ClearSelection();
        if (textBox.FindAncestorOfType<Controls.PropertyViewItem>() is { Focusable: true } propertyViewItem)
        {
            propertyViewItem.Focus();
        }
        else
        {
            TopLevel.GetTopLevel(textBox)?.FocusManager?.Focus(null);
        }
    }

    private static void CloseAncestorPopup(StyledElement start)
    {
        StyledElement? current = start;
        while (current is not null)
        {
            if (current is ComboBox comboBox)
            {
                comboBox.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
                return;
            }
            if (current is Popup popup)
            {
                popup.SetCurrentValue(Popup.IsOpenProperty, false);
                if (popup.TemplatedParent is ComboBox parentComboBox)
                {
                    parentComboBox.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
                    return;
                }
            }

            current = current.Parent
                      ?? (current as Visual)?.GetVisualParent() as StyledElement
                      ?? current.TemplatedParent as StyledElement;
        }
    }

    private static Popup? FindAncestorPopup(StyledElement start)
    {
        StyledElement? current = start;
        while (current is not null)
        {
            if (current is Popup popup)
                return popup;

            current = current.Parent
                      ?? (current as Visual)?.GetVisualParent() as StyledElement
                      ?? current.TemplatedParent as StyledElement;
        }
        return null;
    }
}
