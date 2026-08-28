using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using HdrDoctor.Core.Profiles;
using HdrDoctor.Core.Services;
using HdrDoctor.Core.Sources;

namespace HdrDoctor.App.Services;

/// <summary>
/// What the user typed into the sign-in prompt.
/// </summary>
/// <param name="RememberPassword">
/// Whether to write the password to profiles.json. When false the caller keeps it in
/// memory for the session only, and the next run asks again.
/// </param>
public sealed record FtpCredentials(string? Username, string? Password, bool RememberPassword);

/// <summary>
/// Window-owned dialogs, implemented with Avalonia's storage provider.
/// </summary>
public sealed class DialogService(Window owner)
{
    private readonly Window _owner = owner;

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await _owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    /// <summary>
    /// Collects FTP connection details, and returns a profile only once that
    /// connection has actually been made. Null when the user cancels.
    /// </summary>
    /// <remarks>
    /// The connection is proven before the profile is saved rather than at the first
    /// scan, because a saved profile that has never worked is indistinguishable in the
    /// dropdown from one that has — and the usual cause is a typo the user can still
    /// see on screen.
    /// </remarks>
    /// <returns>
    /// The profile, always carrying the password that worked, and whether the user
    /// asked for it to be stored. The caller decides what reaches profiles.json — the
    /// password is worth keeping for the session either way.
    /// </returns>
    public async Task<(InstallProfile Profile, bool RememberPassword)?> AddFtpProfileAsync()
    {
        var name = new TextBox { PlaceholderText = "My Switch" };
        var host = new TextBox { PlaceholderText = "192.168.1.42" };
        var port = new NumericUpDown
        {
            Value = 5000,
            Minimum = 1,
            Maximum = 65535,
            Increment = 1,
            FormatString = "0",
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 140,
        };
        var username = new TextBox { PlaceholderText = "anonymous" };
        var password = new TextBox { PasswordChar = '•' };

        // InstallProfile.Password is stored in the clear, so the checkbox says so
        // rather than leaving the user to find out from profiles.json.
        var remember = new CheckBox { Content = "Remember the password (saved as plain text)" };

        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            IsVisible = false,
        };

        var cancel = new Button { Content = "Cancel", MinWidth = 96, IsCancel = true };
        var connect = new Button
        {
            Content = "Connect and save",
            MinWidth = 96,
            IsDefault = true,
            Classes = { "accent" },
        };

        var dialog = new Window
        {
            Title = "Add an FTP install",
            SizeToContent = SizeToContent.Height,
            Width = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Icon = _owner.Icon,
        };

        var fields = LabelledFields(6);

        AddRow(fields, 0, "Name", name);
        AddRow(fields, 1, "Address", host);
        AddRow(fields, 2, "Port", port);
        AddRow(fields, 3, "Username", username);
        AddRow(fields, 4, "Password", password);

        Grid.SetRow(remember, 5);
        Grid.SetColumn(remember, 1);
        fields.Children.Add(remember);

        (InstallProfile Profile, bool RememberPassword)? result = null;
        var connecting = false;
        using var cts = new CancellationTokenSource();

        void Report(string message, bool isError)
        {
            status.Text = message;
            status.IsVisible = true;
            status.Classes.Set("muted", !isError);
            status.Foreground = isError
                ? Avalonia.Application.Current?.FindResource("SeverityCriticalBrush") as IBrush
                : null;
        }

        async void OnConnect(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (connecting)
            {
                return;
            }

            var address = (host.Text ?? string.Empty).Trim();

            // An FTP client shows "ftp://192.168.1.42:5000/", and that is what people
            // paste. Take the address out of it rather than failing to resolve it.
            var scheme = address.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0)
            {
                address = address[(scheme + 3)..];
            }

            address = address.Trim('/');

            var colon = address.LastIndexOf(':');
            if (colon > 0 &&
                int.TryParse(address[(colon + 1)..], out var pasted) &&
                pasted is >= 1 and <= 65535)
            {
                port.Value = pasted;
                address = address[..colon];
            }

            host.Text = address;

            if (address.Length == 0)
            {
                Report("Enter the address the FTP server on your Switch is showing.", isError: true);
                host.Focus();
                return;
            }

            connecting = true;
            connect.IsEnabled = false;
            Report("Connecting…", isError: false);

            var label = string.IsNullOrWhiteSpace(name.Text) ? address : name.Text.Trim();
            var user = string.IsNullOrWhiteSpace(username.Text) ? null : username.Text.Trim();
            var secret = string.IsNullOrEmpty(password.Text) ? null : password.Text;
            var number = (int)(port.Value ?? 5000);

            try
            {
                // Connects and proves the server will list the card before this
                // returns, so reaching the next line means the details work.
                await using var probe = await FtpSource.ConnectAsync(
                    address, number, user, secret, cts.Token);

                // Carries the password regardless of the checkbox: the caller keeps it
                // for the session and only writes it out if the user asked.
                result = (
                    InstallProfile.ForFtp(label, address, number, user, secret),
                    remember.IsChecked == true);

                dialog.Close();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                Report($"Could not connect: {error.Message}", isError: true);
            }
            finally
            {
                connecting = false;
                connect.IsEnabled = true;
            }
        }

        connect.Click += OnConnect;
        cancel.Click += (_, _) => dialog.Close();

        // Closing mid-connect would otherwise leave the attempt running against a
        // window that is gone.
        dialog.Closing += (_, _) => cts.Cancel();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 20,
            Children =
            {
                new TextBlock
                {
                    Text = "Add an FTP install",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = "Start the FTP server on your Switch — sys-ftpd or ftpd — and enter the "
                           + "address it shows. This install will be read-only: fixes and reinstalls stay "
                           + "disabled over FTP.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Classes = { "muted" },
                },
                fields,
                status,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, connect },
                },
            },
        };

        var owner = ResolveOwner();

        if (owner is null)
        {
            return null;
        }

        await dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>
    /// Asks for the login to an FTP profile that would not accept the one it has.
    /// Null when the user cancels.
    /// </summary>
    /// <param name="error">
    /// What the server said last time, or null on the first ask. A profile whose
    /// password was never stored fails once by design, so the first prompt is not
    /// framed as an error.
    /// </param>
    public async Task<FtpCredentials?> AskFtpCredentialsAsync(InstallProfile profile, string? error)
    {
        var username = new TextBox { Text = profile.Username, PlaceholderText = "anonymous" };
        var password = new TextBox { PasswordChar = '•' };
        var remember = new CheckBox { Content = "Remember the password (saved as plain text)" };

        var fields = LabelledFields(3);

        AddRow(fields, 0, "Username", username);
        AddRow(fields, 1, "Password", password);

        Grid.SetRow(remember, 2);
        Grid.SetColumn(remember, 1);
        fields.Children.Add(remember);

        FtpCredentials? result = null;

        var cancel = new Button { Content = "Cancel", MinWidth = 96, IsCancel = true };
        var signIn = new Button
        {
            Content = "Sign in",
            MinWidth = 96,
            IsDefault = true,
            Classes = { "accent" },
        };

        var dialog = new Window
        {
            Title = "Sign in",
            SizeToContent = SizeToContent.Height,
            Width = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Icon = _owner.Icon,
        };

        signIn.Click += (_, _) =>
        {
            result = new FtpCredentials(
                string.IsNullOrWhiteSpace(username.Text) ? null : username.Text.Trim(),
                string.IsNullOrEmpty(password.Text) ? null : password.Text,
                remember.IsChecked == true);

            dialog.Close();
        };

        cancel.Click += (_, _) => dialog.Close();

        var body = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 20,
            Children =
            {
                new TextBlock
                {
                    Text = $"Sign in to {profile.Name}",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = $"ftp://{profile.Host}:{profile.Port}/ would not accept the login this profile has.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Classes = { "muted" },
                },
            },
        };

        if (error is not null)
        {
            body.Children.Add(new TextBlock
            {
                Text = error,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = Avalonia.Application.Current?.FindResource("SeverityCriticalBrush") as IBrush,
            });
        }

        body.Children.Add(fields);
        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, signIn },
        });

        dialog.Content = body;
        dialog.Opened += (_, _) => password.Focus();

        var owner = ResolveOwner();

        if (owner is null)
        {
            return null;
        }

        await dialog.ShowDialog(owner);
        return result;
    }

    public async Task<string?> PickFileAsync(string title, IReadOnlyList<string>? extensions = null)
    {
        var filters = extensions is null
            ? null
            : new List<FilePickerFileType>
            {
                new("Logs") { Patterns = [.. extensions.Select(e => $"*.{e}")] },
                new("All files") { Patterns = ["*"] },
            };

        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = filters,
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedName)
    {
        var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "txt",
        });

        return file?.TryGetLocalPath();
    }

    public async Task ShowMessageAsync(string title, string message) =>
        await ShowAsync(title, message, "Close", null, destructive: false);

    public async Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive) =>
        await ShowAsync(title, message, "Cancel", confirmLabel, destructive);

    private async Task<bool> ShowAsync(
        string title,
        string message,
        string dismissLabel,
        string? confirmLabel,
        bool destructive)
    {
        var result = false;

        var dismiss = new Button
        {
            Content = dismissLabel,
            MinWidth = 96,
            IsDefault = destructive || confirmLabel is null,
            IsCancel = true,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { dismiss },
        };

        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.Height,
            Width = 560,
            MaxHeight = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Icon = _owner.Icon,
        };

        if (confirmLabel is not null)
        {
            var confirm = new Button
            {
                Content = confirmLabel,
                MinWidth = 96,
                Classes = { destructive ? "destructive" : "accent" },
            };

            confirm.Click += (_, _) =>
            {
                result = true;
                dialog.Close();
            };

            buttons.Children.Add(confirm);
        }

        dismiss.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 20,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new ScrollViewer
                {
                    MaxHeight = 380,
                    Content = new SelectableTextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 21,
                    },
                },
                buttons,
            },
        };

        var owner = ResolveOwner();

        if (owner is null)
        {
            dialog.Show();
            return false;
        }

        await dialog.ShowDialog(owner);
        return result;
    }


    /// <summary>
    /// Lists the emulator settings that would change and returns the ones the user
    /// kept, or null if they cancelled.
    /// </summary>
    public async Task<IReadOnlyList<SettingChange>?> ConfirmSettingsAsync(
        string emulatorName,
        SettingsPlan plan)
    {
        var boxes = new List<(CheckBox Box, SettingChange Change)>();

        var list = new StackPanel { Spacing = 12 };

        foreach (var change in plan.Changes)
        {
            var box = new CheckBox
            {
                Content = $"{change.DisplayName}  —  {change.Summary}",
                IsChecked = true,
                IsEnabled = change.IsOptional,
            };

            boxes.Add((box, change));

            var row = new StackPanel { Spacing = 2, Children = { box } };

            row.Children.Add(new TextBlock
            {
                Text = $"currently {change.Current}",
                FontSize = 11,
                Margin = new Avalonia.Thickness(28, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Classes = { "muted" },
            });

            if (change.Caveat is not null)
            {
                row.Children.Add(new TextBlock
                {
                    Text = change.Caveat,
                    FontSize = 11,
                    Margin = new Avalonia.Thickness(28, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    Classes = { "muted" },
                });
            }

            list.Children.Add(row);
        }

        foreach (var cache in plan.PptcCaches)
        {
            list.Children.Add(new TextBlock
            {
                Text = $"Deletes the PPTC cache at {cache}",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Avalonia.Application.Current?.FindResource("SeverityCriticalBrush") as IBrush,
            });
        }

        IReadOnlyList<SettingChange>? result = null;

        var cancel = new Button { Content = "Cancel", MinWidth = 96, IsCancel = true };
        var apply = new Button
        {
            Content = "Apply",
            MinWidth = 96,
            IsDefault = true,
            Classes = { plan.PptcCaches.Count > 0 ? "destructive" : "accent" },
        };

        var dialog = new Window
        {
            Title = "Apply emulator settings",
            SizeToContent = SizeToContent.Height,
            Width = 560,
            MaxHeight = 700,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Icon = _owner.Icon,
        };

        apply.Click += (_, _) =>
        {
            result = [.. boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Change)];
            dialog.Close();
        };

        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 20,
            Children =
            {
                new TextBlock
                {
                    Text = $"Apply emulator settings to {emulatorName}",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = "These are written to the config file with highest precedence, which is the per-game config first, then global. "
                           + "The file is backed up alongside itself as .bak first.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Classes = { "muted" },
                },
                new ScrollViewer { MaxHeight = 420, Content = list },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, apply },
                },
            },
        };

        var owner = ResolveOwner();

        if (owner is null)
        {
            return null;
        }

        await dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>A label-and-field grid, as both credential dialogs lay one out.</summary>
    private static Grid LabelledFields(int rows) => new()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", rows))),
        ColumnSpacing = 12,
        RowSpacing = 8,
    };

    private static void AddRow(Grid fields, int row, string label, Control field)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Classes = { "muted" },
        };

        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        fields.Children.Add(text);
        fields.Children.Add(field);
    }

    /// <summary>
    /// The window a dialog should hang off. Falls back to the lifetime's main window
    /// because startup options run dialogs before this service's window is visible.
    /// </summary>
    private Window? ResolveOwner() =>
        _owner.IsVisible
            ? _owner
            : (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
