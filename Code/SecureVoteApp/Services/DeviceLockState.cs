using System;

namespace SecureVoteApp.Services;

public class DeviceLockState
{
    private bool _isLocked;

    public bool IsLocked
    {
        get => _isLocked;
        set => SetLocked(value);
    }

    public event Action<bool>? LockStateChanged;

    public void SetLocked(bool isLocked)
    {
        if (_isLocked == isLocked)
        {
            return;
        }

        _isLocked = isLocked;
        LockStateChanged?.Invoke(_isLocked);
    }
}
