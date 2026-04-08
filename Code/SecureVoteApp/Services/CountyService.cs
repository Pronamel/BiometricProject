using System;

namespace SecureVoteApp.Services;

/// <summary>
/// Simple singleton service to store and share the selected county across the app.
/// This simulates what would normally be stored in user session or extracted from database after login.
/// </summary>
public class CountyService
{
    private static CountyService? _instance;
    public static CountyService Instance => _instance ??= new CountyService();
    
    private string _selectedCounty = string.Empty;
    
    /// <summary>
    /// Gets or sets the currently selected county
    /// </summary>
    public string SelectedCounty
    {
        get => _selectedCounty;
        set
        {
            _selectedCounty = value;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] County updated to: {value}");
        }
    }

    /// <summary>
    /// Clears the selected county (for logout scenarios)
    /// </summary>
    public void ClearCounty()
    {
        SelectedCounty = string.Empty;
    }
    
    /// <summary>
    /// Validates that a county is currently selected
    /// </summary>
    public bool HasValidCounty => !string.IsNullOrWhiteSpace(_selectedCounty);
    
    public CountyService() { }
}