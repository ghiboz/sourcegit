﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SourceGit.Models
{
    public interface IAvatarHost
    {
        void OnAvatarResourceChanged(string email);
    }

    public static partial class AvatarManager
    {
        public static string SelectedServer
        {
            get;
            set;
        } = "https://www.gravatar.com/avatar/";

        static AvatarManager()
        {
            _storePath = Path.Combine(Native.OS.DataDir, "avatars");
            if (!Directory.Exists(_storePath))
                Directory.CreateDirectory(_storePath);

            var icon = AssetLoader.Open(new Uri($"avares://SourceGit/Resources/Images/github.png", UriKind.RelativeOrAbsolute));
            _resources.Add("noreply@github.com", new Bitmap(icon));

            Task.Run(() =>
            {
                while (true)
                {
                    var email = null as string;

                    lock (_synclock)
                    {
                        foreach (var one in _requesting)
                        {
                            email = one;
                            break;
                        }
                    }

                    if (email == null)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    var md5 = GetEmailHash(email);
                    var matchGithubUser = REG_GITHUB_USER_EMAIL().Match(email);
                    var url = matchGithubUser.Success ?
                        $"https://avatars.githubusercontent.com/{matchGithubUser.Groups[2].Value}" :
                        $"{SelectedServer}{md5}?d=404";

                    var localFile = Path.Combine(_storePath, md5);
                    var img = null as Bitmap;
                    try
                    {
                        var client = new HttpClient() { Timeout = TimeSpan.FromSeconds(2) };
                        var task = client.GetAsync(url);
                        task.Wait();

                        var rsp = task.Result;
                        if (rsp.IsSuccessStatusCode)
                        {
                            using (var stream = rsp.Content.ReadAsStream())
                            {
                                using (var writer = File.OpenWrite(localFile))
                                {
                                    stream.CopyTo(writer);
                                }
                            }

                            using (var reader = File.OpenRead(localFile))
                            {
                                img = Bitmap.DecodeToWidth(reader, 128);
                            }
                        }
                    }
                    catch
                    {
                        // ignored
                    }

                    lock (_synclock)
                    {
                        _requesting.Remove(email);
                    }

                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _resources[email] = img;
                        NotifyResourceChanged(email);
                    });
                }
            });
        }

        public static void Subscribe(IAvatarHost host)
        {
            _avatars.Add(host);
        }

        public static void Unsubscribe(IAvatarHost host)
        {
            _avatars.Remove(host);
        }

        public static Bitmap Request(string email, bool forceRefetch)
        {
            if (forceRefetch)
            {
                if (_resources.ContainsKey(email))
                    _resources.Remove(email);

                var localFile = Path.Combine(_storePath, GetEmailHash(email));
                if (File.Exists(localFile))
                    File.Delete(localFile);

                NotifyResourceChanged(email);
            }
            else
            {
                if (_resources.TryGetValue(email, out var value))
                    return value;

                var localFile = Path.Combine(_storePath, GetEmailHash(email));
                if (File.Exists(localFile))
                {
                    try
                    {
                        using (var stream = File.OpenRead(localFile))
                        {
                            var img = Bitmap.DecodeToWidth(stream, 128);
                            _resources.Add(email, img);
                            return img;
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            lock (_synclock)
            {
                if (!_requesting.Contains(email))
                    _requesting.Add(email);
            }

            return null;
        }

        private static string GetEmailHash(string email)
        {
            var lowered = email.ToLower(CultureInfo.CurrentCulture).Trim();
            var hash = MD5.Create().ComputeHash(Encoding.Default.GetBytes(lowered));
            var builder = new StringBuilder();
            foreach (var c in hash)
                builder.Append(c.ToString("x2"));
            return builder.ToString();
        }

        private static void NotifyResourceChanged(string email)
        {
            foreach (var avatar in _avatars)
            {
                avatar.OnAvatarResourceChanged(email);
            }
        }

        private static readonly object _synclock = new object();
        private static readonly string _storePath;
        private static readonly List<IAvatarHost> _avatars = new List<IAvatarHost>();
        private static readonly Dictionary<string, Bitmap> _resources = new Dictionary<string, Bitmap>();
        private static readonly HashSet<string> _requesting = new HashSet<string>();

        [GeneratedRegex(@"^(?:(\d+)\+)?(.+?)@users\.noreply\.github\.com$")]
        private static partial Regex REG_GITHUB_USER_EMAIL();
    }
}
