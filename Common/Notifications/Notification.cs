/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using QuantConnect.Configuration;
using QuantConnect.Util;
using System.Net;
using System.Net.Mail;

namespace QuantConnect.Notifications
{
    /// <summary>
    /// Local/desktop implementation of messaging system for Lean Engine.
    /// </summary>
    [JsonConverter(typeof(NotificationJsonConverter))]
    public abstract class Notification
    {
        /// <summary>
        /// Method for sending implementations of notification object types.
        /// </summary>
        /// <remarks>SMS, Email and Web are all handled by the QC Messaging Handler. To implement your own notification type implement it here.</remarks>
        public virtual void Send()
        {
            //
        }
    }

    /// <summary>
    /// Web Notification Class
    /// </summary>
    public class NotificationWeb : Notification
    {
        /// <summary>
        /// Optional email headers
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Dictionary<string, string> Headers;

        /// <summary>
        /// Send a notification message to this web address
        /// </summary>
        public string Address;

        /// <summary>
        /// Object data to send.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public object Data;

        /// <summary>
        /// Constructor for sending a notification SMS to a specified phone number
        /// </summary>
        /// <param name="address">Address to send to</param>
        /// <param name="data">Data to send</param>
        /// <param name="headers">Optional headers to use</param>
        public NotificationWeb(string address, object data = null, Dictionary<string, string> headers = null)
        {
            Address = address;
            Data = data;
            Headers = headers;
        }
    }

    /// <summary>
    /// Sms Notification Class
    /// </summary>
    public class NotificationSms : Notification
    {
        /// <summary>
        /// Send a notification message to this phone number
        /// </summary>
        public string PhoneNumber;

        /// <summary>
        /// Message to send. Limited to 160 characters
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Message;

        /// <summary>
        /// Constructor for sending a notification SMS to a specified phone number
        /// </summary>
        /// <param name="number"></param>
        /// <param name="message"></param>
        public NotificationSms(string number, string message)
        {
            PhoneNumber = number;
            Message = message;
        }
    }

    /// <summary>
    /// Email notification data.
    /// </summary>
    public class NotificationEmail : Notification
    {
        /// <summary>
        /// Optional email headers
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Dictionary<string, string> Headers;

        /// <summary>
        /// Send to address:
        /// </summary>
        public string Address;

        /// <summary>
        /// Email subject
        /// </summary>
        public string Subject;

        /// <summary>
        /// Message to send.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Message;

        /// <summary>
        /// Email Data
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Data;

        /// <summary>
        /// Default constructor for sending an email notification
        /// </summary>
        /// <param name="address">Address to send to. Will throw <see cref="ArgumentException"/> if invalid
        /// <see cref="Validate.EmailAddress"/></param>
        /// <param name="subject">Subject of the email. Will set to <see cref="string.Empty"/> if null</param>
        /// <param name="message">Message body of the email. Will set to <see cref="string.Empty"/> if null</param>
        /// <param name="data">Data to attach to the email. Will set to <see cref="string.Empty"/> if null</param>
        /// <param name="headers">Optional email headers to use</param>
        public NotificationEmail(string address, string subject = "", string message = "", string data = "", Dictionary<string, string> headers = null)
        {
            if (!Validate.EmailAddress(address))
            {
                throw new ArgumentException(Messages.NotificationEmail.InvalidEmailAddress(address));
            }

            Address = address;
            Data = data ?? string.Empty;
            Message = message ?? string.Empty;
            Subject = subject ?? string.Empty;
            Headers = headers;
        }

        // send the email
        public override void Send()
        {
            MailMessage message = new MailMessage(Address, Address, Subject, Message);
            SmtpClient emailClient = new SmtpClient(Config.Get("smtp-client", "smtp.gmail.com")) {
                Port = Config.GetInt("smtp-port", 587),
                Credentials = new NetworkCredential(Config.Get("email-address", ""), Config.Get("email-password", "")),
                EnableSsl = true,
            };
            emailClient.Send(message);
        }
    }

    /// <summary>
    /// Telegram notification data
    /// </summary>
    public class NotificationTelegram : Notification
    {
        /// <summary>
        /// Send a notification message to this user on Telegram
        /// Can be either a personal ID or Group ID.
        /// </summary>
        public string Id;

        /// <summary>
        /// Message to send. Limited to 4096 characters
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Message;

        /// <summary>
        /// Token to use
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Token;

        /// <summary>
        /// Constructor for sending a telegram notification to a specific User ID
        /// or group ID. Note: The bot must have an open chat with the user or be
        /// added to the group for messages to deliver.
        /// </summary>
        /// <param name="id">User Id or Group Id to send the message too</param>
        /// <param name="message">Message to send</param>
        /// <param name="token">Bot token to use, if null defaults to "telegram-token"
        /// in config on send</param>
        public NotificationTelegram(string id, string message, string token = null)
        {
            Id = id;
            Message = message;
            Token = token;
        }
    }
}
