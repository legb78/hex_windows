using System.Diagnostics.CodeAnalysis;
using HexWin.Diagnostics;

namespace HexWin.Transcription;

/// <summary>
/// Détient le moteur, le charge à la demande et le libère après inactivité.
///
/// <para><b>Le rechargement est déclenché à l'enfoncement de la touche</b>, pas
/// au relâchement : il se déroule donc pendant que l'utilisateur parle. Sur une
/// phrase de deux secondes, les trois secondes de chargement sont presque
/// entièrement masquées, et il ne reste qu'un court reliquat au lieu d'une
/// attente complète.</para>
///
/// <para>Un verrou sérialise chargements et libérations : sans lui, une
/// libération déclenchée par le minuteur pourrait survenir entre le moment où
/// un appelant récupère le moteur et celui où il l'utilise.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Coquille : charge une bibliothèque native et pilote un minuteur. La décision de libérer est dans IdlePolicy, qui est testée.")]
internal sealed class EngineHost : IDisposable
{
    private readonly string _modelPath;
    private readonly string _provider;
    private readonly int _threads;
    private readonly IdlePolicy _policy;
    private readonly SessionLog _log;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly System.Threading.Timer? _idleCheck;

    private ParakeetEngine? _engine;
    private DateTime _lastUse = DateTime.UtcNow;
    private volatile bool _busy;
    private bool _disposed;

    public EngineHost(string modelPath, string provider, int threads, IdlePolicy policy, SessionLog log)
    {
        _modelPath = modelPath;
        _provider = provider;
        _threads = threads;
        _policy = policy;
        _log = log;

        if (_policy.IsEnabled)
        {
            // Vérifié quatre fois par délai : assez fin pour libérer sans
            // traîner, assez rare pour ne rien coûter.
            TimeSpan tick = TimeSpan.FromMilliseconds(Math.Max(_policy.Timeout.TotalMilliseconds / 4, 5_000));
            _idleCheck = new System.Threading.Timer(_ => ReleaseIfIdle(), null, tick, tick);
        }
    }

    public bool IsLoaded => _engine is not null;

    /// <summary>
    /// Lance le chargement sans attendre. Appelé dès l'enfoncement de la
    /// touche pour que le travail se fasse pendant que l'utilisateur parle.
    /// </summary>
    public void BeginLoad()
    {
        if (_engine is not null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await GetAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or DllNotFoundException)
            {
                // L'échec sera signalé à la transcription, qui l'attend.
                _log.Write($"préchargement impossible : {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Rend le moteur, le chargeant si nécessaire. Plusieurs appels
    /// simultanés ne provoquent qu'un seul chargement.
    /// </summary>
    public async Task<ParakeetEngine> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_engine is null)
            {
                var chrono = System.Diagnostics.Stopwatch.StartNew();

                ParakeetEngine engine = await Task
                    .Run(() => ParakeetEngine.Load(_modelPath, _provider, _threads), cancellationToken)
                    .ConfigureAwait(false);

                await engine.WarmUpAsync(cancellationToken).ConfigureAwait(false);

                _engine = engine;
                _log.Write($"modèle chargé en {chrono.Elapsed.TotalSeconds:F1} s");
            }

            _lastUse = DateTime.UtcNow;
            return _engine;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Signale qu'une dictée commence ou s'achève. Tant qu'elle dure, le
    /// modèle ne peut pas être libéré, même si le délai d'inactivité tombe.
    /// </summary>
    public void SetBusy(bool busy)
    {
        _busy = busy;

        if (!busy)
        {
            _lastUse = DateTime.UtcNow;
        }
    }

    private void ReleaseIfIdle()
    {
        if (_engine is null || !_policy.ShouldUnload(DateTime.UtcNow - _lastUse, _busy))
        {
            return;
        }

        if (!_gate.Wait(TimeSpan.Zero))
        {
            // Un chargement ou une transcription est en cours : on réessaiera
            // au prochain passage plutôt que d'attendre en bloquant.
            return;
        }

        try
        {
            // Le contrôle est refait sous verrou : l'état a pu changer entre
            // la première vérification et l'obtention du verrou.
            if (_engine is not null && _policy.ShouldUnload(DateTime.UtcNow - _lastUse, _busy))
            {
                _engine.Dispose();
                _engine = null;
                _log.Write($"modèle libéré après {_policy.Timeout.TotalMinutes:F0} min sans usage");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _idleCheck?.Dispose();
        _engine?.Dispose();
        _engine = null;
        _gate.Dispose();
    }
}
