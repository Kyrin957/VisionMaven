using Prism.Commands;
using Prism.Mvvm;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;

namespace VisionMaven.App.ViewModels;

/// <summary>登录视图模型。</summary>
public sealed class LoginViewModel : BindableBase
{
    private readonly IAuthenticationService _authentication;
    private string _userName = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public LoginViewModel(IAuthenticationService authentication)
    {
        _authentication = authentication;
        LoginCommand = new DelegateCommand(async () => await LoginAsync().ConfigureAwait(false), CanLogin)
            .ObservesProperty(() => UserName)
            .ObservesProperty(() => Password)
            .ObservesProperty(() => IsBusy);
    }

    /// <summary>登录成功请求关闭窗口。</summary>
    public event EventHandler<bool>? CloseRequested;

    public DelegateCommand LoginCommand { get; }

    public string UserName
    {
        get => _userName;
        set => SetProperty(ref _userName, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    private string _password = string.Empty;

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                RaisePropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    private bool CanLogin()
        => !IsBusy && !string.IsNullOrWhiteSpace(UserName) && !string.IsNullOrWhiteSpace(Password);

    private async Task LoginAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = await _authentication.LoginAsync(UserName.Trim(), Password, CancellationToken.None)
                .ConfigureAwait(false);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "登录失败";
                Password = string.Empty;
                return;
            }

            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>强制修改密码视图模型。</summary>
public sealed class ChangePasswordViewModel : BindableBase
{
    private readonly IAuthenticationService _authentication;
    private readonly IUserContext _user;
    private string _currentPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;
    private string _errorMessage = string.Empty;

    public ChangePasswordViewModel(IAuthenticationService authentication, IUserContext user)
    {
        _authentication = authentication;
        _user = user;
        SubmitCommand = new DelegateCommand(async () => await SubmitAsync().ConfigureAwait(false));
    }

    public event EventHandler<bool>? CloseRequested;

    public DelegateCommand SubmitCommand { get; }

    public string CurrentPassword
    {
        get => _currentPassword;
        set => SetProperty(ref _currentPassword, value);
    }

    public string NewPassword
    {
        get => _newPassword;
        set => SetProperty(ref _newPassword, value);
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set => SetProperty(ref _confirmPassword, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                RaisePropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsForced => _user.Current?.MustChangePassword == true;

    private async Task SubmitAsync()
    {
        ErrorMessage = string.Empty;
        var session = _user.Current;
        if (session is null)
        {
            ErrorMessage = "会话已失效";
            return;
        }

        if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
        {
            ErrorMessage = "两次输入的新密码不一致";
            return;
        }

        try
        {
            var success = await _authentication
                .ChangePasswordAsync(session.UserId, CurrentPassword, NewPassword, CancellationToken.None)
                .ConfigureAwait(false);

            if (!success)
            {
                ErrorMessage = "当前密码不正确";
                return;
            }

            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
