using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Networking;

namespace Thry.VRChatAssetInstaller
{
    public class VAI
    {
        private static PackageCollection s_assetCollection = new PackageCollection();
        private static bool s_startedLoading = false;
        private static bool s_isLoading = false;
        const string DATA_URL = "https://raw.githubusercontent.com/Thryrallo/VRChat-Assets-Installer/master/listing.json";

        [Serializable]
        public enum AssetType
        {
            UNITYPACKAGE, UPM
        }

        [Serializable]
        public enum AssetCategory
        {
            AVATAR, WORLD, BOTH
        }

        [Serializable]
        public class AssetInfo
        {
            // Lowercase because of json
            public string type;
            public string vrc;
            public bool upmInstallFromUnitypackage;
            public string packageId;
            public string manifest;
            public string guid;
            public string unitypackageRegex;
            public string git;
            public string author;
            public string name;
            public string description;
            public AssetType Type;
            public AssetCategory Category;
            public bool IsUIExpaned = false;
            public bool IsInstalled;
            public bool IsBeingModified;
            public bool IsBeingInstalled;
            public bool HasUpdate;
            public UnityEditor.PackageManager.PackageInfo UPM;
        }

        [Serializable]
        private class PackageCollection
        {
            public AssetInfo[] curated = new AssetInfo[0];
            public AssetInfo[] other = new AssetInfo[0];
        }

        public static void Reload()
        {
            LoadAssetsListings();
        }

        public static AssetInfo[] CuratedAssets
        {
            get
            {
                if (!s_startedLoading)
                    LoadAssetsListings();
                return s_assetCollection.curated;
            }
        }

        public static AssetInfo[] OtherAssets
        {
            get
            {
                if (!s_startedLoading)
                    LoadAssetsListings();
                return s_assetCollection.other;
            }
        }

        public static bool StartedLoading
        {
            get
            {
                return s_startedLoading;
            }
        }

        public static bool IsLoading
        {
            get
            {
                return s_isLoading;
            }
        }

        static AssetCategory[] s_supportedAssetCategories;
        public static AssetCategory[] SupportedAssetCategories
        {
            get
            {
                if(s_supportedAssetCategories == null)
                {
                    bool hasAvatarSDK = FindTypeByFullName("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor") != null;
                    bool hasWorldSDK = FindTypeByFullName("VRC.SDK3.Components.VRCSceneDescriptor") != null;
                    List<AssetCategory> categories = new List<AssetCategory>();
                    if (hasAvatarSDK) categories.Add(AssetCategory.AVATAR);
                    if (hasWorldSDK) categories.Add(AssetCategory.WORLD);
                    if (categories.Count > 0) categories.Add(AssetCategory.BOTH);
                    s_supportedAssetCategories = categories.ToArray();
                }
                return s_supportedAssetCategories;
            }
        }

        private static void LoadAssetsListings()
        {
            s_startedLoading = true;
            s_isLoading = true;
            var installedPackages = Client.List(true);
            WebHelper.DownloadStringASync(DATA_URL, (string s) => {
                s_assetCollection = JsonUtility.FromJson<PackageCollection>(s);
                while(installedPackages.IsCompleted == false)
                    Thread.Sleep(100);
                s_assetCollection.curated.ToList().ForEach(m => LoadInfoForAsset(m, installedPackages));
                s_assetCollection.other.ToList().ForEach(m => LoadInfoForAsset(m, installedPackages));
                s_assetCollection.curated = s_assetCollection.curated.Where(m => SupportedAssetCategories.Contains(m.Category)).OrderBy(m => m.name).ToArray();
                s_assetCollection.other = s_assetCollection.other.Where(m => SupportedAssetCategories.Contains(m.Category)).OrderBy(m => m.name).ToArray();
                s_isLoading = false;
            });
        }

        static void LoadInfoForAsset(AssetInfo p, ListRequest installedPackages)
        {
            // Unity JsonUtility does not support enums, so we have to do it manually
            // Fucking unity !
            p.Type = (AssetType)Enum.Parse(typeof(AssetType), p.type.ToUpper());
            p.Category = AssetCategory.BOTH;
            if(p.vrc == "A") p.Category = AssetCategory.AVATAR;
            if(p.vrc == "W") p.Category = AssetCategory.WORLD;
            if(p.Type != AssetType.UNITYPACKAGE)
            {
                var package = installedPackages.Result.FirstOrDefault(pac => pac.name == p.packageId);
                p.UPM = package;
                p.IsInstalled = package != null;
                if (p.IsInstalled) p.HasUpdate = package.versions.all.Length > 0 && package.versions.latest != package.version;
            }else
            {
                string path = AssetDatabase.GUIDToAssetPath(p.guid);
                p.IsInstalled = string.IsNullOrWhiteSpace(path) == false && (File.Exists(path) || Directory.Exists(path));
            }
        }

        enum RequestType
        {
            INSTALL,
            UNINSTALL
        }

        struct UPMRequest
        {
            public RequestType Type;
            public Request Request;
            public AssetInfo Package;
        }

        static List<UPMRequest> s_requests = new List<UPMRequest>();
        static List<AssetInfo> s_packagesToEmbed = new List<AssetInfo>();
        public static void InstallAsset(AssetInfo package)
        {
            if(package.Type != AssetType.UNITYPACKAGE && !package.upmInstallFromUnitypackage) InstallAssetInternal(package, package.git + ".git");
            else GetUnityPackageUrlFromReleases(package, (string url) => InstallAssetInternal(package, url));
        }

        static void InstallAssetInternal(AssetInfo asset, string url)
        {
            asset.IsBeingModified = true;
            asset.IsBeingInstalled = true;
            if(asset.Type != AssetType.UNITYPACKAGE && !asset.upmInstallFromUnitypackage)
            {
                Debug.Log("[UPM] Downloading & Installing " + url);
                var request = Client.Add(url);
                UPMRequest upmRequest = new UPMRequest();
                upmRequest.Type = RequestType.INSTALL;
                upmRequest.Request = request;
                upmRequest.Package = asset;
                s_requests.Add(upmRequest);
                PlayerPrefs.SetString("ThryUPMEmbed", asset.packageId);
                EditorApplication.update += CheckRequests;
            }else
            {
                Debug.Log("[Unitypackage] Downloading & Installing " + url);
                var filenname = url.Substring(url.LastIndexOf("/") + 1);
                var path = Path.Combine(Application.temporaryCachePath, filenname);
                WebHelper.DownloadFileASync(url, path, (string path2) =>
                {
                    AssetDatabase.ImportPackage(path2, false);
                    asset.IsInstalled = true;
                    asset.IsBeingModified = false;
                });
            }
        }
        
        [Serializable]
        class GitHubAsset
        {
            public string browser_download_url;
        }

        [Serializable]
        class GitHubRelease
        {
            public GitHubAsset[] assets;
        }

        static void GetUnityPackageUrlFromReleases(AssetInfo package, Action<string> callback)
        {
            var parts = package.git.Split(new string[]{"/"}, StringSplitOptions.RemoveEmptyEntries);
            var repo = parts[parts.Length - 2] + "/" + parts[parts.Length - 1];
            WebHelper.DownloadStringASync($"https://api.github.com/repos/{repo}/releases/latest", (string s) =>
            {
                try
                {
                    GitHubRelease release = JsonUtility.FromJson<GitHubRelease>(s);
                    bool useRegex = string.IsNullOrWhiteSpace(package.unitypackageRegex) == false;
                    foreach(var asset in release.assets)
                    {
                        if(string.IsNullOrWhiteSpace(asset.browser_download_url) == false && asset.browser_download_url.EndsWith(".unitypackage") &&
                            (useRegex == false || Regex.Match(asset.browser_download_url, package.unitypackageRegex).Success))
                        {
                            callback(asset.browser_download_url);
                        }
                    }
                }catch(Exception e)
                {
                    Debug.LogError("Error while downloading latest release of " + package.git + ".");
                    Debug.LogError(e);
                }
            });
        }

        public static void RemoveAsset(AssetInfo asset)
        {
            asset.IsBeingModified = true;
            asset.IsBeingInstalled = false;
            if(asset.Type != AssetType.UNITYPACKAGE)
            {
                var request = Client.Remove(asset.packageId);
                UPMRequest upmRequest = new UPMRequest();
                upmRequest.Type = RequestType.UNINSTALL;
                upmRequest.Request = request;
                upmRequest.Package = asset;
                EditorApplication.update += CheckRequests;
                // Deleting Manually because Client.Remove does not work on embedded packages
                if(asset.UPM.assetPath.StartsWith("Packages/"))
                {
                    asset.IsInstalled = false;
                    asset.IsBeingModified = false;
                }
            }else
            {
                string path = AssetDatabase.GUIDToAssetPath(asset.guid);
                if(EditorUtility.DisplayDialog("Remove asset", "Do you want to delete the folder " + path + "?", "Yes", "No"))
                {
                    AssetDatabase.DeleteAsset(path);
                    asset.IsInstalled = false;
                    asset.IsBeingModified = false;
                    AssetDatabase.Refresh();
                }
            }
        }

        static void CheckRequests()
        {
            for (int i = 0; i < s_requests.Count; i++)
            {
                UPMRequest request = s_requests[i];
                if (request.Request.IsCompleted)
                {
                    if (request.Request.Status == StatusCode.Success)
                    {
                        request.Package.IsBeingModified = false;
                        s_requests.RemoveAt(i);
                        i--;
                        if(request.Type == RequestType.INSTALL)
                        {
                            Debug.Log("[Package] Installed '" + request.Package.packageId);
                        }
                        else if(request.Type == RequestType.UNINSTALL)
                        {
                            Debug.Log("[Package] Uninstalled '" + request.Package.packageId);
                        }
                    }
                    else if (request.Request.Status >= StatusCode.Failure)
                    {
                        s_requests.RemoveAt(i);
                        i--;
                        // Try manually deleting the package
                        if(request.Type == RequestType.UNINSTALL && request.Package.UPM != null)
                        {
                            string path = request.Package.UPM.assetPath;
                            Debug.LogWarning($"[Package] UPM Removing failed, trying to delete the package manually from {path}.");
                            AssetDatabase.DeleteAsset(path);
                        }else if(request.Type == RequestType.INSTALL || request.Type == RequestType.UNINSTALL)
                        {
                            Debug.LogError(request.Request.Error);
                            request.Package.IsBeingModified = false;
                            request.Package.IsInstalled = request.Type != RequestType.INSTALL;
                        }
                    }
                }
            }
            if (s_requests.Count == 0)
                EditorApplication.update -= CheckRequests;
        }

        static Type FindTypeByFullName(string fullname)
        {
            return (from assembly in AppDomain.CurrentDomain.GetAssemblies()
                    from type in assembly.GetTypes()
                    where type.FullName == fullname
                    select type).FirstOrDefault();
        }
    }

    public class WebHelper
    {
        [InitializeOnLoad]
        public class MainThreader
        {
            private struct CallData
            {
                public object action;
                public object[] arguments;
            }
            static List<CallData> queue;

            static MainThreader()
            {
                queue = new List<CallData>();
                EditorApplication.update += Update;
            }

            public static void Call(object action, params object[] args)
            {
                if (action == null)
                    return;
                CallData data = new CallData();
                data.action = action;
                data.arguments = args;
                if (args == null || args.Length == 0 || args[0] == null)
                    data.arguments = new object[] { "" };
                else
                    data.arguments = args;
                queue.Add(data);
            }

            public static void Update()
            {
                if (queue.Count > 0)
                {
                    try
                    {
                        if(queue[0].action is Action<string>) ((Action<string>)queue[0].action).DynamicInvoke(queue[0].arguments);
                        if(queue[0].action is Action<byte[]>) ((Action<byte[]>)queue[0].action).DynamicInvoke(queue[0].arguments);
                    }
                    catch(Exception e) {
                        Debug.LogWarning("[Thry] Error during WebRequest: " + e.ToString());
                    }
                    queue.RemoveAt(0);
                }
            }
        }

        public static string DownloadString(string url)
        {
            return DownloadAsString(url);
        }

        public static void DownloadStringASync(string url, Action<string> callback)
        {
            DownloadAsStringASync(url, delegate (object o, DownloadStringCompletedEventArgs e)
            {
                if (e.Cancelled || e.Error != null)
                {
                    Debug.LogWarning(e.Error);
                    MainThreader.Call(callback, null);
                }
                else
                    MainThreader.Call(callback, e.Result);
            });
        }

        private static void SetCertificate()
        {
            ServicePointManager.ServerCertificateValidationCallback =
        delegate (object s, X509Certificate certificate,
                 X509Chain chain, SslPolicyErrors sslPolicyErrors)
        { return true; };
        }

        private static string DownloadAsString(string url)
        {
            SetCertificate();
            string contents = null;
            try
            {
                using (var wc = new System.Net.WebClient())
                    contents = wc.DownloadString(url);
            }catch(WebException e)
            {
                Debug.LogError(e);
            }
            return contents;
        }

        private static void DownloadAsStringASync(string url, Action<object, DownloadStringCompletedEventArgs> callback)
        {
            SetCertificate();
            using (var wc = new System.Net.WebClient())
            {
                wc.Headers["User-Agent"] = "Mozilla/4.0 (Compatible; Windows NT 5.1; MSIE 6.0)";
                wc.DownloadStringCompleted += new DownloadStringCompletedEventHandler(callback);
                wc.DownloadStringAsync(new Uri(url));
            }
        }

        public static void DownloadFileASync(string url, string path, Action<string> callback)
        {
            DownloadAsBytesASync(url, delegate (object o, DownloadDataCompletedEventArgs a)
            {
                if (a.Cancelled || a.Error != null)
                    MainThreader.Call(callback, null);
                else
                {
                    WriteBytesToFile(a.Result, path);
                    MainThreader.Call(callback, path);
                }
            });
        }

        private static void DownloadAsBytesASync(string url, Action<object, DownloadDataCompletedEventArgs> callback)
        {
            SetCertificate();
            using (var wc = new System.Net.WebClient())
            {
                wc.DownloadDataCompleted += new DownloadDataCompletedEventHandler(callback);
                url = FixUrl(url);
                wc.DownloadDataAsync(new Uri(url));
            }
        }

        public static string FixUrl(string url)
        {
            if (!url.StartsWith("http"))
                url = "http://" + url;
            url = url.Replace("\\","/");
            if (System.Text.RegularExpressions.Regex.IsMatch(url, @"^https?:\/[^\/].*"))
                url = url.Replace(":/", "://");
            return url;
        }

        public static bool WriteBytesToFile(byte[] bytes, string path)
        {
            if (!File.Exists(path)) CreateFileWithDirectories(path);
            try
            {
                using (var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.Log("Exception caught in process: " + ex.ToString());
                return false;
            }
        }

        public static void CreateFileWithDirectories(string path)
        {
            string dir_path = Path.GetDirectoryName(path);
            if (dir_path != "")
                Directory.CreateDirectory(dir_path);
            File.Create(path).Close();
        }
    }
}