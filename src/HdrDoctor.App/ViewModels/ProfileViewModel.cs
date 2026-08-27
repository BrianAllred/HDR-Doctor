using CommunityToolkit.Mvvm.ComponentModel;
using HdrDoctor.Core.Model;
using HdrDoctor.Core.Profiles;

namespace HdrDoctor.App.ViewModels;

/// <summary>
/// A saved install as the profile dropdown shows it.
/// </summary>
public sealed partial class ProfileViewModel(InstallProfile profile) : ViewModelBase
{
    [ObservableProperty]
    public partial InstallProfile Profile { get; set; } = profile;

    public string Id => Profile.Id;

    public string Name => Profile.Name;

    public bool IsAvailable => Profile.IsAvailable;

    /// <summary>
    /// What the dropdown shows. An unavailable profile (ejected SD card, for example)
    /// shows an empty slot so the user doesn't think it's missing.
    /// </summary>
    public string Display => IsAvailable
        ? $"{Name}  ·  {Describe()}"
        : $"{Name}  ·  unavailable";

    public string Detail => Profile.UnavailableReason ?? Describe();

    private string Describe() => Profile.Kind switch
    {
        ProfileKind.Ftp => $"{Profile.Host}:{Profile.Port} (read-only)",
        _ when Profile.IsFtpMount => $"{Profile.Path} (FTP mount, read-only)",
        _ => Profile.Path ?? "no folder set",
    };

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(Detail));
    }
}
