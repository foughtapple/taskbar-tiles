// Curated, documented Windows Settings deep links with user-facing search aliases.
// Source: https://learn.microsoft.com/windows/apps/develop/launch/launch-settings
// This is not an embedded Windows Search process or its private index/ranking API.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
namespace TaskbarTiles
{
    static class WindowsSettingsCatalog
    {
        // name | URI suffix | page breadcrumb | common query phrases
        static readonly string[] Pages = {
            "Windows Settings||Settings home|control panel settings;windows preferences",
            "Display settings|display|System > Display|screen resolution;change resolution;monitor layout;multiple monitors;screen brightness;display scaling;text scaling;screen orientation;hdr display",
            "Advanced display settings|display-advanced|System > Display (Advanced display on supported Windows versions)|refresh rate;monitor hz;display frequency;screen hertz",
            "Night light|nightlight|System > Display > Night light|blue light;warm screen;night mode",
            "Graphics settings|display-advancedgraphics|System > Display > Graphics|gpu preferences;graphics card;graphics performance;choose gpu",
            "Default graphics settings|display-advancedgraphics-default|System > Display > Graphics|hardware accelerated gpu scheduling;hags;variable refresh rate",
            "Sound settings|sound|System > Sound|audio settings;speakers;headphones;microphone;output device;input device;volume",
            "Volume mixer|apps-volume|System > Sound > Volume mixer|app volume;per app audio;individual application sound;mute app",
            "Sound devices|sound-devices|System > Sound > All sound devices|enable audio device;disable sound device;manage speakers",
            "Microphone properties|sound-defaultinputproperties|System > Sound > Input|microphone level;input volume;mic volume;recording device",
            "Speaker and headphone properties|sound-defaultoutputproperties|System > Sound > Output|output volume;spatial sound;audio format;headphone properties",
            "Notifications|notifications|System > Notifications|notification settings;notification banners;alerts;do not disturb",
            "Focus assist|quiethours|System > Focus / Notifications|quiet hours;focus mode;notification interruptions",
            "Power and sleep|powersleep|System > Power|power settings;sleep timeout;screen timeout;turn off screen;power mode",
            "Battery saver|batterysaver|System > Power & battery (battery-equipped devices)|battery life;energy saver;battery settings",
            "Storage settings|storagesense|System > Storage|disk space;drive space;storage usage;temporary files",
            "Storage Sense|storagepolicies|System > Storage > Storage Sense|automatic cleanup;clean temporary files;free disk space",
            "Storage cleanup recommendations|storagerecommendations|System > Storage|cleanup recommendations;unused files;free space recommendations",
            "Disks and volumes|disksandvolumes|System > Storage > Disks & volumes|manage drives;disk volumes;drive properties",
            "Default save locations|savelocations|System > Storage|where new content is saved;default save drive",
            "Multitasking and Snap windows|multitasking|System > Multitasking|snap layouts;snap assist;virtual desktops;alt tab settings",
            "Projecting to this PC|project|System > Projecting to this PC|wireless display;project screen;miracast",
            "Remote Desktop|remotedesktop|System > Remote Desktop|rdp;remote access;remote connection",
            "Clipboard settings|clipboard|System > Clipboard|clipboard history;copy paste history;clipboard sync",
            "About this PC|about|System > About|device specifications;computer name;pc name;ram;processor;windows version;system information",
            "Bluetooth settings|bluetooth|Bluetooth & devices|pair device;bluetooth headphones;bluetooth mouse;connect controller",
            "Connected devices|connecteddevices|Bluetooth & devices > Devices|device list;remove device;paired devices",
            "Printers and scanners|printers|Bluetooth & devices > Printers & scanners|add printer;print queue;scanner;default printer",
            "Mouse settings|mousetouchpad|Bluetooth & devices > Mouse|mouse speed;cursor speed;scroll speed;primary mouse button;pointer sensitivity",
            "Touchpad settings|devices-touchpad|Bluetooth & devices > Touchpad (supported devices)|trackpad;touchpad gestures;tap to click",
            "Touch settings|devices-touch|Bluetooth & devices > Touch (supported devices)|touchscreen;touch screen;touch gestures",
            "Pen and Windows Ink|pen|Bluetooth & devices > Pen & Windows Ink|stylus;surface pen;pen handwriting;ink settings",
            "Typing settings|typing|Time & language > Typing|autocorrect;spelling;text suggestions;typing preferences",
            "USB settings|usb|Bluetooth & devices > USB|usb notifications;usb connection",
            "AutoPlay settings|autoplay|Bluetooth & devices > AutoPlay|removable drive;memory card;autoplay defaults",
            "Camera settings|camera|Bluetooth & devices > Cameras|camera device;webcam settings;camera properties",
            "Network and internet|network-status|Network & internet|network settings;internet connection;network status;ip address",
            "Wi-Fi settings|network-wifi|Network & internet > Wi-Fi (Wi-Fi-equipped devices)|wifi;wireless network;connect wifi;wi fi",
            "Saved Wi-Fi networks|network-wifisettings|Network & internet > Manage known networks|forget wifi;known networks;saved wireless networks",
            "Ethernet settings|network-ethernet|Network & internet > Ethernet|wired network;lan connection;ethernet adapter",
            "VPN settings|network-vpn|Network & internet > VPN|virtual private network;add vpn",
            "Proxy settings|network-proxy|Network & internet > Proxy|proxy server;proxy configuration",
            "Mobile hotspot|network-mobilehotspot|Network & internet > Mobile hotspot|share internet;wifi hotspot;wi fi hotspot",
            "Airplane mode|network-airplanemode|Network & internet > Airplane mode|flight mode;disable wireless",
            "Advanced network settings|network-advancedsettings|Network & internet > Advanced network settings|network adapters;network reset;adapter options",
            "Installed apps|appsfeatures|Apps > Installed apps|uninstall apps;remove program;apps and features;installed programs",
            "Default apps|defaultapps|Apps > Default apps|default browser;file associations;open with;default program",
            "Startup apps|startupapps|Apps > Startup|startup programs;disable startup;launch at login;sign in apps",
            "Optional Windows features|optionalfeatures|System / Apps > Optional features|add feature;optional features;install windows feature",
            "Video playback|videoplayback|Apps > Video playback|video settings;video processing;streaming video",
            "Personalisation|personalization|Personalisation|personalization;desktop appearance;personalise;customize windows",
            "Desktop background|personalization-background|Personalisation > Background|wallpaper;desktop picture;background image;slideshow",
            "Colours and dark mode|personalization-colors|Personalisation > Colours|colors;dark theme;light mode;accent colour;accent color;transparency",
            "Themes|themes|Personalisation > Themes|desktop theme;change theme;windows theme",
            "Lock screen|lockscreen|Personalisation > Lock screen|lock screen picture;spotlight;screen saver",
            "Start menu settings|personalization-start|Personalisation > Start|start recommendations;recently added apps;start pins",
            "Taskbar settings|taskbar|Personalisation > Taskbar|auto hide taskbar;autohide;taskbar alignment;system tray;taskbar behaviour;taskbar behavior",
            "Fonts|fonts|Personalisation > Fonts|install font;font preview;font list",
            "Touch keyboard|personalization-touchkeyboard|Personalisation > Touch keyboard|onscreen keyboard theme;touch keyboard size",
            "Sign-in options|signinoptions|Accounts > Sign-in options|password;windows hello;pin login;fingerprint;face recognition",
            "Your account|yourinfo|Accounts > Your info|account picture;local account;account information",
            "Email and app accounts|emailandaccounts|Accounts > Email & accounts|microsoft account;add account;email accounts",
            "Other users|otherusers|Accounts > Other users|add user;family account;remove user;user accounts",
            "Work or school account|workplace|Accounts > Access work or school|work account;school account;organisation account",
            "Date and time|dateandtime|Time & language > Date & time|time zone;clock;time sync;set date",
            "Language settings|regionlanguage|Time & language > Language & region|display language;language pack;keyboard language",
            "Region and formats|regionformatting|Time & language > Language & region|country;regional format;date format;regional settings",
            "Advanced keyboard settings|keyboard-advanced|Time & language > Typing|keyboard layout;input language;language hotkey",
            "Speech settings|speech|Time & language > Speech|speech language;text to speech;voice settings",
            "Text size and accessibility display|easeofaccess-display|Accessibility > Text size / Display|larger text;make text bigger;accessibility display",
            "Mouse pointer and touch accessibility|easeofaccess-mousepointer|Accessibility > Mouse pointer & touch|cursor size;pointer colour;pointer color;touch indicator",
            "Accessibility keyboard|easeofaccess-keyboard|Accessibility > Keyboard|sticky keys;filter keys;toggle keys;on screen keyboard",
            "Magnifier|easeofaccess-magnifier|Accessibility > Magnifier|screen zoom;magnification",
            "Colour filters|easeofaccess-colorfilter|Accessibility > Colour filters|color blindness;grayscale;colour blindness;greyscale",
            "Contrast themes|easeofaccess-highcontrast|Accessibility > Contrast themes|high contrast;contrast settings",
            "Narrator|easeofaccess-narrator|Accessibility > Narrator|screen reader;read aloud",
            "Visual effects|easeofaccess-visualeffects|Accessibility > Visual effects|animation effects;transparency effects;reduce animations",
            "Microphone privacy|privacy-microphone|Privacy & security > Microphone|microphone permissions;allow microphone;mic access",
            "Camera privacy|privacy-webcam|Privacy & security > Camera|camera permissions;webcam access;allow camera",
            "Location privacy|privacy-location|Privacy & security > Location|location services;location permissions",
            "Search permissions|search-permissions|Privacy & security > Search permissions|safe search;search history;search permissions",
            "Windows Search settings|search|Privacy & security > Searching Windows|indexing;search index;indexed files;search folders",
            "Game Mode|gaming-gamemode|Gaming > Game Mode|gaming performance;game mode settings",
            "Game Bar|gaming-gamebar|Gaming > Game Bar|xbox game bar;gaming overlay",
            "Game capture settings|gaming-gamedvr|Gaming > Captures|game recording;record gameplay;background recording;game dvr",
            "Windows Update|windowsupdate|Windows Update|check for updates;update windows;windows updates",
            "Update history|windowsupdate-history|Windows Update > Update history|installed updates;update history;recent updates",
            "Optional updates|windowsupdate-optionalupdates|Windows Update > Optional updates|driver updates;optional drivers",
            "Advanced update options|windowsupdate-options|Windows Update > Advanced options|pause updates;update preferences",
            "Windows Security|windowsdefender|Privacy & security > Windows Security|antivirus;virus protection;firewall;defender",
            "Windows activation|activation|System > Activation|product key;activate windows;activation status",
            "Recovery settings|recovery|System > Recovery|reset pc;advanced startup;recovery options",
            "Troubleshooting|troubleshoot|System > Troubleshoot|troubleshoot problems;troubleshooters;fix problems",
            "Device encryption|deviceencryption|Privacy & security > Device encryption (supported devices)|disk encryption;device encryption;bitlocker",
            "Delivery Optimisation|delivery-optimization|Windows Update > Delivery Optimisation|delivery optimization;update downloads;download updates other pcs"
        };
        internal static List<SearchItem> Read()
        {
            var result = new List<SearchItem>();
            foreach (var line in Pages)
            {
                var p = line.Split('|');
                var item = SearchLogic.FromEntry(new FavouriteEntry { Name = p[0], Target = "ms-settings:" + p[1], Group = "Settings",
                    IconPath = @"%WINDIR%\ImmersiveControlPanel\SystemSettings.exe" }, "Settings", p[2]);
                item.Keywords = p[3]; result.Add(item);
            }
            return result;
        }
    }
    static class SearchMatch
    {
        static string Normal(string text) { return Regex.Replace((text ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim(); }
        internal static bool Typo(string a, string b)
        {
            if (a.Length < 5 || b.Length < 5 || a.Length > 40 || b.Length > 40 || Math.Abs(a.Length - b.Length) > 1) return false;
            if (a.Length == b.Length)
            {
                var differences = Enumerable.Range(0, a.Length).Where(i => a[i] != b[i]).ToArray();
                return differences.Length <= 1 || differences.Length == 2 && differences[1] == differences[0] + 1 &&
                    a[differences[0]] == b[differences[1]] && a[differences[1]] == b[differences[0]];
            }
            string shorter = a.Length < b.Length ? a : b, longer = a.Length < b.Length ? b : a;
            int x = 0, y = 0, skipped = 0;
            while (x < shorter.Length && y < longer.Length)
            { if (shorter[x] == longer[y]) { x++; y++; } else { skipped++; y++; if (skipped > 1) return false; } }
            return true;
        }
        internal static int Score(SearchItem item, string query)
        {
            string q = Normal(query), name = Normal(item.Name);
            if (q.Length == 0) return item.Kind == "Favourites" ? 80 : item.Kind == "Windows" ? 70 : 20;
            if (name == q) return 1400;
            string[] words = q.Split(' ').Where(w => w.Length > 0).Take(12).ToArray();
            if (item.Kind == "Settings" && words.Length > 1)
                words = words.Where(w => !new[] { "change", "adjust", "open", "my", "the", "please" }.Contains(w)).ToArray();
            string all = name + " " + Normal(item.Kind) + " " + Normal(item.Detail) + " " + Normal(item.Keywords);
            var tokens = all.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int approximate = 0;
            foreach (string word in words)
            {
                if (all.IndexOf(word, StringComparison.Ordinal) >= 0) continue;
                if (tokens.Any(t => Typo(word, t))) approximate++; else return -1;
            }
            if (approximate > 1) return -1;
            string[] aliases = (item.Keywords ?? "").Split(';').Select(Normal).ToArray();
            int score = name.StartsWith(q, StringComparison.Ordinal) ? 1100 : aliases.Contains(q) ? 1050 :
                name.IndexOf(q, StringComparison.Ordinal) >= 0 ? 900 : words.All(w => name.Contains(w)) ? 780 : 500;
            if (item.Kind == "Favourites") score += 40; else if (item.Kind == "Apps") score += 30; else if (item.Kind == "Windows") score += 20;
            return score - approximate * 180;
        }
    }
}
