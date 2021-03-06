﻿using Kahla.Server.Data;
using Microsoft.ApplicationInsights;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WebPush;

namespace Kahla.Server.Services
{
    public class ThirdPartyPushService
    {
        private readonly KahlaDbContext _dbContext;
        private readonly IConfiguration _configuration;
        private readonly WebPushClient _webPushClient;
        private readonly ILogger _logger;
        private readonly TelemetryClient _telemetry;

        public ThirdPartyPushService(
            KahlaDbContext dbContext,
            IConfiguration configuration,
            WebPushClient webPushClient,
            ILogger<ThirdPartyPushService> logger,
            TelemetryClient telemetry)
        {
            _dbContext = dbContext;
            _configuration = configuration;
            _webPushClient = webPushClient;
            _logger = logger;
            _telemetry = telemetry;
        }

        public async Task PushAsync(string receiverId, string triggerEmail, string payload)
        {
            var devices = await _dbContext.Devices.Where(t => t.UserId == receiverId).ToListAsync();
            string vapidPublicKey = _configuration.GetSection("VapidKeys")["PublicKey"];
            string vapidPrivateKey = _configuration.GetSection("VapidKeys")["PrivateKey"];
            // Push to all devices.

            var pushTasks = new List<Task>();
            foreach (var device in devices)
            {
                async Task PushToDevice()
                {
                    try
                    {
                        var pushSubscription = new PushSubscription(device.PushEndpoint, device.PushP256DH, device.PushAuth);
                        var vapidDetails = new VapidDetails("mailto:" + triggerEmail, vapidPublicKey, vapidPrivateKey);
                        _logger.LogInformation($"Trying to call WebPush API to push a new event to {receiverId}, Event content is '{payload}', Device ID is {device.Id}");
                        await _webPushClient.SendNotificationAsync(pushSubscription, payload, vapidDetails);
                    }
                    catch (WebPushException e)
                    {
                        _dbContext.Devices.Remove(device);
                        _logger.LogCritical(e, "A WebPush error occured while calling WebPush API: " + e.Message);
                        _telemetry.TrackException(e);
                    }
                    catch (Exception e)
                    {
                        _telemetry.TrackException(e);
                        _logger.LogCritical(e, "An error occured while calling WebPush API: " + e.Message);
                    }
                }
                pushTasks.Add(PushToDevice());
            }
            await Task.WhenAll(pushTasks);
        }
    }
}
