using DynamoDbFusion.Core.Exceptions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace DynamoDbFusion.Core.Services;

/// <summary>
/// Circuit breaker service for handling service failures and preventing cascading failures
/// </summary>
public class CircuitBreakerService
{
    private readonly ILogger<CircuitBreakerService> _logger;
    private readonly ConcurrentDictionary<string, CircuitBreaker> _circuitBreakers;

    public CircuitBreakerService(ILogger<CircuitBreakerService> logger)
    {
        _logger = logger;
        _circuitBreakers = new ConcurrentDictionary<string, CircuitBreaker>();
    }

    /// <summary>
    /// Executes an operation with circuit breaker protection
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    /// <param name="operationKey">Unique key for the operation</param>
    /// <param name="operation">The operation to execute</param>
    /// <param name="fallback">Fallback operation when circuit is open</param>
    /// <param name="config">Circuit breaker configuration</param>
    /// <returns>The result of the operation or fallback</returns>
    public async Task<T> ExecuteAsync<T>(
        string operationKey,
        Func<Task<T>> operation,
        Func<Task<T>>? fallback = null,
        CircuitBreakerConfig? config = null)
    {
        var circuitBreaker = _circuitBreakers.GetOrAdd(operationKey, 
            key => new CircuitBreaker(key, config ?? CircuitBreakerConfig.Default, _logger));

        return await circuitBreaker.ExecuteAsync(operation, fallback);
    }

    /// <summary>
    /// Gets the current state of a circuit breaker
    /// </summary>
    /// <param name="operationKey">Operation key</param>
    /// <returns>Circuit breaker state information</returns>
    public CircuitBreakerState GetState(string operationKey)
    {
        if (_circuitBreakers.TryGetValue(operationKey, out var circuitBreaker))
        {
            return circuitBreaker.GetState();
        }

        return new CircuitBreakerState
        {
            OperationKey = operationKey,
            State = CircuitState.Closed,
            FailureCount = 0,
            LastFailureTime = null,
            NextAttemptTime = null
        };
    }

    /// <summary>
    /// Manually resets a circuit breaker
    /// </summary>
    /// <param name="operationKey">Operation key</param>
    public void Reset(string operationKey)
    {
        if (_circuitBreakers.TryGetValue(operationKey, out var circuitBreaker))
        {
            circuitBreaker.Reset();
            _logger.LogInformation("Circuit breaker for operation {OperationKey} was manually reset", operationKey);
        }
    }

    /// <summary>
    /// Gets statistics for all circuit breakers
    /// </summary>
    /// <returns>Circuit breaker statistics</returns>
    public Dictionary<string, CircuitBreakerState> GetAllStates()
    {
        return _circuitBreakers.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.GetState()
        );
    }
}

/// <summary>
/// Individual circuit breaker implementation
/// </summary>
internal class CircuitBreaker
{
    private readonly string _operationKey;
    private readonly CircuitBreakerConfig _config;
    private readonly ILogger _logger;
    private readonly object _lockObject = new();

    private CircuitState _state = CircuitState.Closed;
    private int _failureCount = 0;
    private DateTime? _lastFailureTime;
    private DateTime? _nextAttemptTime;

    public CircuitBreaker(string operationKey, CircuitBreakerConfig config, ILogger logger)
    {
        _operationKey = operationKey;
        _config = config;
        _logger = logger;
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Func<Task<T>>? fallback)
    {
        lock (_lockObject)
        {
            if (_state == CircuitState.Open)
            {
                if (DateTime.UtcNow < _nextAttemptTime)
                {
                    _logger.LogWarning("Circuit breaker for {OperationKey} is OPEN, rejecting request", _operationKey);
                    
                    if (fallback != null)
                    {
                        return fallback().Result;
                    }
                    
                    throw new ServiceUnavailableException(_operationKey, 
                        $"Circuit breaker is open for operation '{_operationKey}'", 
                        _nextAttemptTime.Value - DateTime.UtcNow);
                }

                // Transition to half-open
                _state = CircuitState.HalfOpen;
                _logger.LogInformation("Circuit breaker for {OperationKey} transitioning to HALF-OPEN", _operationKey);
            }
        }

        try
        {
            var result = await operation();
            
            lock (_lockObject)
            {
                if (_state == CircuitState.HalfOpen)
                {
                    // Success in half-open state, reset to closed
                    Reset();
                    _logger.LogInformation("Circuit breaker for {OperationKey} reset to CLOSED after successful test", _operationKey);
                }
                else if (_failureCount > 0)
                {
                    // Reduce failure count on success
                    _failureCount = Math.Max(0, _failureCount - 1);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            lock (_lockObject)
            {
                _failureCount++;
                _lastFailureTime = DateTime.UtcNow;

                if (_state == CircuitState.HalfOpen || _failureCount >= _config.FailureThreshold)
                {
                    // Open the circuit
                    _state = CircuitState.Open;
                    _nextAttemptTime = DateTime.UtcNow.Add(_config.OpenTimeout);
                    
                    _logger.LogWarning("Circuit breaker for {OperationKey} opened after {FailureCount} failures. Next attempt at {NextAttempt}",
                        _operationKey, _failureCount, _nextAttemptTime);
                }
                else
                {
                    _logger.LogWarning("Circuit breaker for {OperationKey} recorded failure {FailureCount}/{Threshold}",
                        _operationKey, _failureCount, _config.FailureThreshold);
                }
            }

            // If circuit is open and we have a fallback, use it
            if (_state == CircuitState.Open && fallback != null)
            {
                try
                {
                    return await fallback();
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError(fallbackEx, "Fallback operation also failed for {OperationKey}", _operationKey);
                    throw new ServiceUnavailableException(_operationKey, 
                        "Both primary operation and fallback failed");
                }
            }

            throw;
        }
    }

    public CircuitBreakerState GetState()
    {
        lock (_lockObject)
        {
            return new CircuitBreakerState
            {
                OperationKey = _operationKey,
                State = _state,
                FailureCount = _failureCount,
                LastFailureTime = _lastFailureTime,
                NextAttemptTime = _nextAttemptTime,
                FailureThreshold = _config.FailureThreshold,
                OpenTimeoutSeconds = _config.OpenTimeout.TotalSeconds
            };
        }
    }

    public void Reset()
    {
        lock (_lockObject)
        {
            _state = CircuitState.Closed;
            _failureCount = 0;
            _lastFailureTime = null;
            _nextAttemptTime = null;
        }
    }
}

/// <summary>
/// Circuit breaker configuration
/// </summary>
public class CircuitBreakerConfig
{
    /// <summary>
    /// Number of failures before opening the circuit
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Time to wait before attempting to close the circuit
    /// </summary>
    public TimeSpan OpenTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Default configuration
    /// </summary>
    public static CircuitBreakerConfig Default => new();

    /// <summary>
    /// Conservative configuration for critical operations
    /// </summary>
    public static CircuitBreakerConfig Conservative => new()
    {
        FailureThreshold = 3,
        OpenTimeout = TimeSpan.FromMinutes(5)
    };

    /// <summary>
    /// Aggressive configuration for fast-failing operations
    /// </summary>
    public static CircuitBreakerConfig Aggressive => new()
    {
        FailureThreshold = 10,
        OpenTimeout = TimeSpan.FromSeconds(30)
    };
}

/// <summary>
/// Circuit breaker states
/// </summary>
public enum CircuitState
{
    /// <summary>
    /// Circuit is closed, requests flow normally
    /// </summary>
    Closed,

    /// <summary>
    /// Circuit is open, requests are rejected
    /// </summary>
    Open,

    /// <summary>
    /// Circuit is testing if service has recovered
    /// </summary>
    HalfOpen
}

/// <summary>
/// Circuit breaker state information
/// </summary>
public class CircuitBreakerState
{
    public string OperationKey { get; set; } = string.Empty;
    public CircuitState State { get; set; }
    public int FailureCount { get; set; }
    public DateTime? LastFailureTime { get; set; }
    public DateTime? NextAttemptTime { get; set; }
    public int FailureThreshold { get; set; }
    public double OpenTimeoutSeconds { get; set; }
} 