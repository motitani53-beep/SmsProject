using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WebApplication1.Services;

public class SenderPhoneNumberService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SenderPhoneNumberService> _logger;
    private readonly IMemoryCache _memoryCache;
    private const string CacheKeyPrefix = "sender_pool_";
    private const int CacheExpirationMinutes = 30;

    public SenderPhoneNumberService(
        IConfiguration configuration,
        ILogger<SenderPhoneNumberService> logger,
        IMemoryCache memoryCache)
    {
        _configuration = configuration;
        _logger = logger;
        _memoryCache = memoryCache;
    }

    /// <summary>
    /// Gets the next phone number for a campaign using round-robin access.
    /// For random sender type, returns a number from the cached pool.
    /// For manual types, returns the sender_value directly.
    /// </summary>
    public string GetNextPhoneNumberForCampaign(int campaignId, int messageIndex, string senderType, string? senderValue)
    {
        // Handle manual_number and manual_string types
        var st = senderType.ToLowerInvariant();
        if (st == "manual_number" || st == "specific" || st == "manual_string")
        {
            if (!string.IsNullOrEmpty(senderValue))
            {
                return senderValue;
            }
            _logger.LogWarning("Manual sender type specified but sender_value is empty for campaign {CampaignId}", campaignId);
            return "0000000000"; // Default fallback
        }

        // Handle random sender type
        if (st == "random")
        {
            var pool = GetOrCreatePhoneNumberPool(campaignId);
            if (pool == null || pool.Count == 0)
            {
                _logger.LogWarning("No phone number pool available for campaign {CampaignId}, using default", campaignId);
                return "0000000000"; // Default fallback
            }

            // Round-robin access using messageIndex
            var phoneNumber = pool[messageIndex % pool.Count];
            _logger.LogDebug("Retrieved phone number {PhoneNumber} for campaign {CampaignId}, message index {MessageIndex}",
                phoneNumber, campaignId, messageIndex);
            
            return phoneNumber;
        }

        // Unknown sender type, return sender_value if available
        if (!string.IsNullOrEmpty(senderValue))
        {
            return senderValue;
        }

        _logger.LogWarning("Unknown sender type '{SenderType}' for campaign {CampaignId}, using default", senderType, campaignId);
        return "0000000000"; // Default fallback
    }

    /// <summary>
    /// Gets or creates a phone number pool for a campaign from cache.
    /// If not in cache, randomly selects a range, expands it, shuffles it, and caches it.
    /// </summary>
    private List<string>? GetOrCreatePhoneNumberPool(int campaignId)
    {
        var cacheKey = $"{CacheKeyPrefix}{campaignId}";

        // Try to get from cache
        if (_memoryCache.TryGetValue(cacheKey, out List<string>? cachedPool) && cachedPool != null)
        {
            _logger.LogDebug("Retrieved phone number pool from cache for campaign {CampaignId} with {Count} numbers",
                campaignId, cachedPool.Count);
            return cachedPool;
        }

        // Pool not in cache, create it
        _logger.LogInformation("Creating new phone number pool for campaign {CampaignId}", campaignId);
        var pool = CreatePhoneNumberPool();

        if (pool == null || pool.Count == 0)
        {
            _logger.LogError("Failed to create phone number pool for campaign {CampaignId}", campaignId);
            return null;
        }

        // Store in cache with sliding expiration
        var cacheOptions = new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(CacheExpirationMinutes),
            Priority = CacheItemPriority.Normal
        };

        _memoryCache.Set(cacheKey, pool, cacheOptions);
        _logger.LogInformation("Created and cached phone number pool for campaign {CampaignId} with {Count} numbers (expires after {Minutes} minutes of inactivity)",
            campaignId, pool.Count, CacheExpirationMinutes);

        return pool;
    }

    /// <summary>
    /// Creates a phone number pool by:
    /// 1. Loading XML configuration
    /// 2. Randomly selecting one range
    /// 3. Expanding the range into a list of all numbers
    /// 4. Shuffling the list
    /// </summary>
    private List<string>? CreatePhoneNumberPool()
    {
        try
        {
            var configPath = _configuration["ConfigPaths:SenderPhoneNumberConfig"];
            if (string.IsNullOrEmpty(configPath))
            {
                _logger.LogError("SenderPhoneNumberConfig path not found in configuration");
                return null;
            }

            if (!File.Exists(configPath))
            {
                _logger.LogError("Sender phone number config file not found at: {Path}", configPath);
                return null;
            }

            // Load XML document
            var doc = XDocument.Load(configPath);
            var ranges = doc.Descendants("range").ToList();

            if (ranges.Count == 0)
            {
                _logger.LogError("No phone number ranges found in XML configuration at: {Path}", configPath);
                return null;
            }

            // Step A: Randomly pick one range
            var random = new Random();
            var selectedRange = ranges[random.Next(ranges.Count)];

            var startStr = selectedRange.Element("start")?.Value;
            var endStr = selectedRange.Element("end")?.Value;

            if (string.IsNullOrEmpty(startStr) || string.IsNullOrEmpty(endStr))
            {
                _logger.LogError("Invalid range in XML: start or end is missing");
                return null;
            }

            if (!long.TryParse(startStr, out var start) || !long.TryParse(endStr, out var end))
            {
                _logger.LogError("Invalid range in XML: start '{Start}' or end '{End}' is not a valid number", startStr, endStr);
                return null;
            }

            if (start > end)
            {
                _logger.LogError("Invalid range in XML: start '{Start}' is greater than end '{End}'", start, end);
                return null;
            }

            _logger.LogInformation("Selected range: {Start} to {End}", start, end);

            // Step B: Expand the range into a list of all possible numbers
            var numbers = new List<string>();
            for (var num = start; num <= end; num++)
            {
                numbers.Add(num.ToString());
            }

            if (numbers.Count == 0)
            {
                _logger.LogError("Expanded range resulted in zero numbers");
                return null;
            }

            _logger.LogInformation("Expanded range to {Count} phone numbers", numbers.Count);

            // Step C: Shuffle the list
            var shuffledNumbers = numbers.OrderBy(x => random.Next()).ToList();

            _logger.LogInformation("Shuffled phone number pool with {Count} numbers", shuffledNumbers.Count);

            return shuffledNumbers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating phone number pool from XML configuration");
            return null;
        }
    }

    /// <summary>
    /// Gets all available phone numbers from the XML config (for informational purposes).
    /// This does not use caching and loads all ranges.
    /// </summary>
    public List<string> GetAllAvailablePhoneNumbers()
    {
        try
        {
            var configPath = _configuration["ConfigPaths:SenderPhoneNumberConfig"];
            if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
            {
                _logger.LogWarning("Sender phone number config file not found at: {Path}", configPath);
                return new List<string>();
            }

            var doc = XDocument.Load(configPath);
            var numbers = new List<string>();

            var ranges = doc.Descendants("range");
            foreach (var range in ranges)
            {
                var startStr = range.Element("start")?.Value;
                var endStr = range.Element("end")?.Value;

                if (string.IsNullOrEmpty(startStr) || string.IsNullOrEmpty(endStr))
                    continue;

                if (long.TryParse(startStr, out var start) && long.TryParse(endStr, out var end))
                {
                    for (var num = start; num <= end; num++)
                    {
                        numbers.Add(num.ToString());
                    }
                }
            }

            return numbers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading all phone numbers from config");
            return new List<string>();
        }
    }
}
