using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TL;

namespace ProjectTraiding.Telegram;

public sealed class TelegramAccountSession : IAsyncDisposable
{
    public enum AccountState
    {
        Disabled, Starting, VerificationCodeRequired, PasswordRequired,
        EmailRequired, EmailCodeRequired, Authorized, Receiving, Error, Stopped
    }

    public enum AnswerResult { Accepted, InvalidInput, Conflict, Disabled, Failed }
    public readonly record struct AuthorizationStatus(AccountState State, string? ExpectedField);

    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramAccountSession> _logger;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _loginGate = new(1, 1);
    private readonly SemaphoreSlim _closeGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource<bool> _authorized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AccountState _state;
    private string? _expectedField;
    private WTelegram.Client? _client;
    private WTelegram.UpdateManager? _manager;

    public TelegramAccountSession(IOptions<TelegramOptions> options, ILogger<TelegramAccountSession> logger)
    {
        _logger = logger;
        try
        {
            _options = options.Value;
            _state = _options.Enabled ? AccountState.Starting : AccountState.Disabled;
        }
        catch (Exception)
        {
            // Ошибка привязки может содержать исходное значение настройки. Не журналируем исключение.
            _options = new TelegramOptions();
            SetErrorLocked();
            _logger.LogError("Telegram: ошибка привязки конфигурации; клиент не создан.");
        }
    }

    public AuthorizationStatus GetStatus()
    {
        lock (_stateLock) return new(_state, _expectedField);
    }

    internal async Task StartAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        using CancellationTokenRegistration registration = stoppingToken.Register(_lifetime.Cancel);
        await _loginGate.WaitAsync(stoppingToken);
        try
        {
            lock (_stateLock)
            {
                if (_state != AccountState.Starting || _client is not null) return;
                if (!ValidateOptions())
                {
                    SetErrorLocked();
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(_options.SessionPath)!);
                Directory.CreateDirectory(Path.GetDirectoryName(_options.UpdateStatePath)!);
                // Штатный журнал библиотеки содержит данные аккаунта, RPC и исходные исключения.
                WTelegram.Helpers.Log = DiscardLibraryLog;
                _client = new WTelegram.Client(_options.ApiId, _options.ApiHash, _options.SessionPath)
                {
                    // Неверный ответ завершает единственную последовательность входа.
                    MaxCodePwdAttempts = 1
                };
                _manager = _client.WithUpdateManager(HandleUpdateAsync, _options.UpdateStatePath);
                _manager.Log = null;
                _client.OnOther += HandleOtherAsync;
            }
            await ContinueLoginAsync(_options.PhoneNumber);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            await FailAsync("ошибка создания клиента или входа");
        }
        finally { _loginGate.Release(); }
    }

    public async Task<AnswerResult> AnswerAsync(string? expectedField, string? value)
    {
        if (string.IsNullOrWhiteSpace(expectedField) || string.IsNullOrWhiteSpace(value))
            return AnswerResult.InvalidInput;
        if (!await _loginGate.WaitAsync(0)) return AnswerResult.Conflict;
        try
        {
            lock (_stateLock)
            {
                if (_state == AccountState.Disabled) return AnswerResult.Disabled;
                if (_client is null || _expectedField is null || _lifetime.IsCancellationRequested)
                    return AnswerResult.Conflict;
                if (!string.Equals(expectedField, _expectedField, StringComparison.Ordinal))
                    return AnswerResult.Conflict;
                _expectedField = null;
                _state = AccountState.Starting;
            }
            // Отмена HTTP-запроса не освобождает gate, пока фактический Login ещё работает.
            await ContinueLoginAsync(value);
            return GetStatus().State == AccountState.Error ? AnswerResult.Failed : AnswerResult.Accepted;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return AnswerResult.Conflict;
        }
        catch (Exception)
        {
            await FailAsync("ошибка продолжения входа; для новой попытки перезапустите Api");
            return AnswerResult.Failed;
        }
        finally { _loginGate.Release(); }
    }

    private async Task ContinueLoginAsync(string value)
    {
        string? requested = await _client!.Login(value).WaitAsync(_lifetime.Token);
        bool unsupported = false;
        lock (_stateLock)
        {
            if (_state is AccountState.Error or AccountState.Stopped || _lifetime.IsCancellationRequested) return;
            _expectedField = requested;
            _state = requested switch
            {
                null => AccountState.Authorized,
                "verification_code" => AccountState.VerificationCodeRequired,
                "password" => AccountState.PasswordRequired,
                "email" => AccountState.EmailRequired,
                "email_verification_code" => AccountState.EmailCodeRequired,
                _ => AccountState.Error
            };
            if (_state == AccountState.Authorized) _authorized.TrySetResult(true);
            if (_state == AccountState.Error)
            {
                SetErrorLocked();
                unsupported = true;
            }
        }
        if (unsupported) await FailAsync("регистрация аккаунта или неизвестный шаг входа не поддерживается");
    }

    internal Task<bool> WaitForAuthorizationAsync(CancellationToken token) => _authorized.Task.WaitAsync(token);
    internal Task WaitUntilFinishedAsync(CancellationToken token) => _finished.Task.WaitAsync(token);

    private static void DiscardLibraryLog(int level, string message) { }
    private Task HandleUpdateAsync(Update update) => Task.CompletedTask;

    private Task HandleOtherAsync(IObject notification)
    {
        if (notification is ReactorError)
            return FailAsync("соединение Telegram завершилось ошибкой");
        return Task.CompletedTask;
    }

    private bool ValidateOptions()
    {
        string? error = null;
        if (!string.IsNullOrEmpty(_options.OutboundProxyUrl))
            error = "OutboundProxyUrl не поддерживается; прямое подключение не выполнялось";
        else if (_options.ApiId <= 0 || _options.ApiHash.Length != 32 || !IsHex(_options.ApiHash)
            || string.IsNullOrWhiteSpace(_options.PhoneNumber))
            error = "требуются корректные ApiId, ApiHash и PhoneNumber";
        else if (!ValidStoragePath(_options.SessionPath) || !ValidStoragePath(_options.UpdateStatePath)
            || string.Equals(Path.GetFullPath(_options.SessionPath), Path.GetFullPath(_options.UpdateStatePath), StringComparison.OrdinalIgnoreCase))
            error = "SessionPath и UpdateStatePath должны быть разными абсолютными путями вне каталога публикации и .git";
        else if (_options.Channels is null || _options.Channels.Length == 0)
            error = "Channels должен содержать публичные имена каналов";
        else
        {
            foreach (string? channel in _options.Channels)
            {
                if (string.IsNullOrWhiteSpace(channel) || channel.TrimStart('@').Length == 0
                    || channel.Contains('/') || channel.Contains('\\') || channel.Contains(' '))
                {
                    error = "Channels содержит некорректное публичное имя";
                    break;
                }
            }
        }
        if (error is null) return true;
        _logger.LogError("Telegram: {ConfigurationError}; клиент не создан.", error);
        return false;
    }

    private static bool IsHex(string value)
    {
        foreach (char character in value)
            if (!Uri.IsHexDigit(character)) return false;
        return true;
    }

    private static bool ValidStoragePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || string.IsNullOrEmpty(Path.GetFileName(path))) return false;
        string fullPath = Path.GetFullPath(path);
        string basePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        if (fullPath.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (string part in fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            if (string.Equals(part, ".git", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private void SetErrorLocked()
    {
        _state = AccountState.Error;
        _expectedField = null;
        _authorized.TrySetResult(false);
        _finished.TrySetResult();
    }

    internal async Task FailAsync(string safeError)
    {
        lock (_stateLock)
        {
            if (_state is AccountState.Disabled or AccountState.Stopped) return;
            SetErrorLocked();
        }
        _logger.LogError("Telegram: {TelegramError}.", safeError);
        await CloseClientAsync();
    }

    internal async Task StopAsync()
    {
        lock (_stateLock)
        {
            if (_state is not AccountState.Disabled and not AccountState.Error) _state = AccountState.Stopped;
            _expectedField = null;
            _authorized.TrySetResult(false);
            _finished.TrySetResult();
        }
        await CloseClientAsync();
    }

    private async Task CloseClientAsync()
    {
        _lifetime.Cancel();
        await _closeGate.WaitAsync();
        try
        {
            WTelegram.Client? client;
            lock (_stateLock)
            {
                client = _client;
                _client = null;
            }
            if (client is null) return;
            client.OnOther -= HandleOtherAsync;
            try { await client.DisposeAsync(); }
            catch (Exception)
            {
                lock (_stateLock) SetErrorLocked();
                _logger.LogError("Telegram: ошибка освобождения клиента.");
            }
        }
        finally { _closeGate.Release(); }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
