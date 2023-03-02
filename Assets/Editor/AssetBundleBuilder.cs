using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using AssetBundleBuild = UnityEditor.AssetBundleBuild;

public class AssetBundleBuilder
{

    [Serializable]
    private class BundleInfo
    {
        public string bundleName;
        public Dictionary<string,string> fileInfo;
        
    }

    private static readonly string ABCacheInfoFileName = "/BundleInfos.txt";
    private static readonly string ABResRootPath = "Asset/AssetData";
    private readonly BuildTarget _buildTarget;
    private readonly string _assetBundleOutPutPath;
    private readonly string _assetBundleCachePath;
    private readonly string _assetBundlePoolPath;

    private readonly string _bundleRelativePath = "publish";
    
    
    
    
    private Dictionary<string, BundleInfo> _cacheInfos;

    private readonly bool _buildAll = false;
    
    public AssetBundleBuilder(BuildTarget buildTarget,bool buildAll)
    {
        _buildTarget = buildTarget;
        _assetBundleOutPutPath = "AssetBundle/" + GetTargetPlatformPath() +"/"+ _bundleRelativePath; //强制资源的输出路径
        _assetBundleCachePath = "AssetBundleCaches/" + GetTargetPlatformPath();
        _assetBundlePoolPath = "AssetBundlePool/" + GetTargetPlatformPath();

        _cacheInfos = new Dictionary<string, BundleInfo>();
        _buildAll = buildAll;
    }

    public string GetTargetPlatformPath()
    {
        if (_buildTarget == BuildTarget.Android)
        {
            return "Android";
        } 
        if (_buildTarget == BuildTarget.iOS)
        {
            return "IOS";
        }
        return "Windows";
    }

    
    public void SetArgs()
    {
        
    }

    public void Build()
    {
        if (!Directory.Exists(_assetBundleOutPutPath))
            Directory.CreateDirectory(_assetBundleOutPutPath);
        if (!Directory.Exists(_assetBundleCachePath))
            Directory.CreateDirectory(_assetBundleCachePath);
        if (Directory.Exists(_assetBundlePoolPath))
            Directory.Delete(_assetBundlePoolPath,true);
        
        Directory.CreateDirectory(_assetBundlePoolPath);
        UnityEditor.AssetDatabase.RemoveUnusedAssetBundleNames();

        if (!_buildAll)
        {
            ReadyLocalCacheBundleInfos();
        }
        
        EditorUtility.DisplayProgressBar("计算差异","",0.1f );
            
        var buildList =  StepCalcAbChange().ToArray();
        
        Debug.LogError($"需要build {buildList.Length} 个bundle");
        EditorUtility.DisplayProgressBar("开始build","",0.2f );
        EditorUtility.DisplayDialog("xx", $"需要build {buildList.Length} 个bundle", "ok");
        
        var stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start(); // 开始监视代码运行时间
        BuildPipeline.BuildAssetBundles(_assetBundlePoolPath , buildList, BuildAssetBundleOptions.ChunkBasedCompression,
            _buildTarget);
        stopwatch.Stop(); // 停止监视
        TimeSpan timespan = stopwatch.Elapsed; // 获取当前实例测量得出的总时间
        string seconds = timespan.TotalSeconds.ToString(); // 总秒数
        Debug.LogError("BuildDiff cost seconds " + seconds);
        EditorUtility.DisplayProgressBar("build 完成  记录信息","",0.5f );
        
        for (int i = 0; i < buildList.Length; i++)
        {
            Debug.LogError($"Build AB -------{buildList[i].assetBundleName}");
        }
        
        //记录新的信息
        if (File.Exists(_assetBundleCachePath + ABCacheInfoFileName))
            File.Delete(_assetBundleCachePath + ABCacheInfoFileName);
        string json = JsonConvert.SerializeObject(_cacheInfos);
        File.WriteAllText(_assetBundleCachePath + ABCacheInfoFileName ,json);
        EditorUtility.DisplayProgressBar("build","",0.7f );
        //处理bundle
        
        CopyAllABToOutPath(buildList);

        CreateVersionFile();
        EditorUtility.DisplayProgressBar("build","",0.9f );
        AssetDatabase.Refresh();
        EditorUtility.ClearProgressBar();
    }


    private void ReadyLocalCacheBundleInfos()
    {
        if (File.Exists(_assetBundleCachePath + ABCacheInfoFileName))
        {
           string data =  File.ReadAllText(_assetBundleCachePath + ABCacheInfoFileName);
           _cacheInfos = JsonConvert.DeserializeObject<Dictionary<string, BundleInfo>>(data);
        }
        else
        {
            Debug.LogError($"没有找到BundleInfo文件---- path----{_assetBundleCachePath + ABCacheInfoFileName}");
        }
       
    }


    private List<AssetBundleBuild> StepCalcAbChange()
    {

        var stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start(); // 开始监视代码运行时间
        
        List<AssetBundleBuild> buildList = new List<AssetBundleBuild>();
        string[] allAbNames = AssetDatabase.GetAllAssetBundleNames();
        
        bool ischanged = false;
        
        for (int i = 0; i < allAbNames.Length; i++)
        {
            string abName = allAbNames[i];
            string[] assetsPaths = AssetDatabase.GetAssetPathsFromAssetBundle(abName);
            ischanged = false;
            
            
            if (!_cacheInfos.ContainsKey(abName))
                ischanged = true;


            if (!ischanged)
            {
                var bundleInfo = _cacheInfos[abName];
                //数量不一致  
                if (bundleInfo.fileInfo.Count != assetsPaths.Length)
                    ischanged = true;

                if (!ischanged)
                {
                    var bundleInfoFiles = bundleInfo.fileInfo;
                    foreach (var path in assetsPaths)
                    {
                        if (!bundleInfoFiles.ContainsKey(path))
                            ischanged = true;
                        var cacheHash = bundleInfoFiles[path];
                        var guid = AssetDatabase.AssetPathToGUID(path);
                        var hash = AssetDatabase.GetAssetDependencyHash(path).ToString();
                        if (hash != cacheHash)
                            ischanged = true;
                    }
                }
            }
        

            if (ischanged)
            {
                buildList.Add(CreatAbBuild(abName,assetsPaths));
                var newInfo = new BundleInfo();
                newInfo.bundleName = abName;
                newInfo.fileInfo = new Dictionary<string, string>();
                foreach (var path in assetsPaths)
                {
                    var guid = AssetDatabase.AssetPathToGUID(path);
                    var hash = AssetDatabase.GetAssetDependencyHash(path).ToString();
                    newInfo.fileInfo.Add(path,hash);
                }
                
                if (_cacheInfos.ContainsKey(abName))
                {
                    _cacheInfos[abName] = newInfo;
                }
                else
                {
                    _cacheInfos.Add(abName,newInfo);
                }
                
            }
            
        }

        stopwatch.Stop(); // 停止监视
        TimeSpan timespan = stopwatch.Elapsed; // 获取当前实例测量得出的总时间
        string seconds = timespan.TotalSeconds.ToString(); // 总秒数
        Debug.Log("BuildDiff cost seconds " + seconds);
        return buildList;
        
    }
    
    private AssetBundleBuild CreatAbBuild(string assetBundleName,string[] assetsPaths)
    {
        
        AssetBundleBuild abb = new AssetBundleBuild
        {
            assetBundleName = assetBundleName,
            assetBundleVariant = null,
            assetNames = assetsPaths,
            addressableNames = null
        };

        return abb;
    }


    public string suffix = ".bytes";
    public string password = "password";
    public bool encrypted = false;
    private void CopyAllABToOutPath(AssetBundleBuild[] builds )
    {
        foreach (var b_build in builds)
        {
            int childFolder = b_build.assetBundleName.LastIndexOf("/");
            if (childFolder > 0)
            {
                string childFolderPath = _assetBundleOutPutPath + "/" + b_build.assetBundleName.Substring(0, childFolder);
                if (!Directory.Exists(childFolderPath))
                {
                    Directory.CreateDirectory(childFolderPath);
                }
            }

            string inputPath = _assetBundlePoolPath + "/" + b_build.assetBundleName;
            string outputPath = _assetBundleOutPutPath + "/" + b_build.assetBundleName + suffix;

            MemoryStream outputStream = new MemoryStream(File.ReadAllBytes(inputPath));
            // if (!string.IsNullOrEmpty(password) && encrypted)
            // {
            //     // encrypted
            //     outputStream = new MemoryStream(XXTEA.Encrypt(
            //         outputStream.ToArray(),
            //         _config.password));
            // }
            File.WriteAllBytes(outputPath, outputStream.ToArray());

            var size = (uint)new FileInfo(outputPath).Length;
            var md5 = GetFileMD5(outputPath);
        }
    }


    public string versionFileName = "vc";
    
    private void CreateVersionFile()
    {
       
    }
    

    
    
    
    /// <summary>
    /// 设置目录下所有AB的名字
    /// </summary>
    public void StepResetAbName()
    {
        var stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start(); // 开始监视代码运行时间

        
        stopwatch.Stop(); // 停止监视
        TimeSpan timespan = stopwatch.Elapsed; // 获取当前实例测量得出的总时间
        string seconds = timespan.TotalSeconds.ToString(); // 总秒数
        Debug.Log("SetABName cost seconds " + seconds);
        EditorUtility.DisplayDialog("Success", $"set assetbundle name success  Used Time {seconds}s !", "Yes");
    }

    private readonly List<string> _fileList = new List<string>();
    public void ResetAbName(string assetRootPath)
    {
        if (!Directory.Exists(assetRootPath))
        {
            Debug.LogError(" assetPath Null : " + assetRootPath);
            return;
        }
        GetAllFiles(assetRootPath, _fileList);

        foreach (var path in _fileList)
        {
            if (path.Contains(".vscode") ||
                path.Contains(".cs") ||
                path.Contains(".shader") ||
                path.Contains("Local") ||
                path.Contains("Shader") ||
                path.Contains("SuperTextMesh") ||
                path.Contains(".meta") ||
                path.Contains(".DS_Store") ||
                path.Contains(".dll"))
            {
                continue;
            }
            
            
            
        }
        
    }
    
    private static int abNameIndex = ABResRootPath.Length + 1;
    public static string GetABNameFormat(string path)
    {
        return path.Substring(abNameIndex + 1, path.LastIndexOf(".", StringComparison.Ordinal) - abNameIndex - 1).ToLower();
    }
    
    public static void GetAllFiles(string directory, List<string> paths)
    {
        if (!Directory.Exists(directory))
        {
            Debug.LogError("Directory Not Exist " + directory);
            return;
        }
        string[] files = Directory.GetFiles(directory);
        string[] directorys = Directory.GetDirectories(directory);
        
        foreach (var t in files)
        {
            string path = t;
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }
            if (path.Contains("\\"))
            {
                var arr = path.Split('\\');
                path = arr[0] + "/" + arr[1];
            }
            paths.Add(path);
        }
        
        foreach (var t in directorys)
        {
            string path = t;
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }
            if (path.Contains("\\"))
            {
                var arr = path.Split('\\');
                path = arr[0] + "/" + arr[1];
            }
            GetAllFiles(path, paths);
        }
        
    }
    
    
    static string GetFileMD5(string path)
    {
        byte[] md5Result;
        using (FileStream fs = new FileStream(path, FileMode.Open))
        {
            MD5 md5 = new MD5CryptoServiceProvider();
            md5Result = md5.ComputeHash(fs);
        }
        
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < md5Result.Length; i++)
        {
            sb.Append(md5Result[i].ToString("x2"));
        }
        return sb.ToString();

    }
}
