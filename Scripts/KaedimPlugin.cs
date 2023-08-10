using UnityEngine;
using UnityEditor;
using System.Text;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using Unity.VisualScripting;

[System.Serializable]
public class AssetList
{
    public List<Asset> assets;
}

[System.Serializable]
public class Asset
{
    public string requestID;
    public List<string> image;
    public List<string> image_tags;
    public List<Iteration> iterations;
    public Texture2D texture;
}

[System.Serializable]
public class Iteration
{
    public string iterationID;
    public Results results;
    public string status;
}

[System.Serializable]
public class Results
{
    public string fbx;
    public string glb;
    public string mtl;
    public string obj;
    public string gltf;
}

[System.Serializable]
public class ResponseData
{
    public string jwt;
    public string message;
}

public class KaedimPlugin : EditorWindow
{
    private class EditorCoroutine
    {
        public IEnumerator Routine { get; private set; }
        public bool IsDone { get; private set; }  // Define the IsDone property here

        public EditorCoroutine(IEnumerator routine)
        {
            this.Routine = routine;
            this.IsDone = false; // Set the initial value for IsDone
        }

        public void MarkAsDone()
        {
            this.IsDone = true;
        }
    }

    private List<EditorCoroutine> _coroutines = new List<EditorCoroutine>();
    string devID = "";
    string apiKey = "";
    string jwt = "";
    string state = "";
    string refreshToken = "";
    static string domain = "https://api.kaedim3d.com";
    List<Asset> assets = new List<Asset>();
    Asset selectedAsset = null;
    string error = "";
    string uploadError = "";
    Texture2D selectedImage;
    string imagePath;
    string quality = "standard/high/ultra";
    string polycount = "< 30,000";

    // Add menu named "KaedimPlugin" to the Window menu
    [MenuItem("Window/Kaedim Plugin")]
    static void Init()
    {
        // Get existing open window or if none, make a new one:
        KaedimPlugin window = (KaedimPlugin)EditorWindow.GetWindow(typeof(KaedimPlugin));
        window.Show();
    }

    void OnEnable()
    {
        // Load saved IDs when the window is enabled
        state = "login";
        devID = EditorPrefs.GetString("DevID", "");
        apiKey = EditorPrefs.GetString("APIKey", "");
        refreshToken = EditorPrefs.GetString("RefreshToken", "");
        jwt = EditorPrefs.GetString("jwt", "");
    }

    private Vector2 scrollPosition = Vector2.zero;

    void OnGUI()
    {
        if (state == "login")
        {
            GUILayout.Label("Enter your credentials", EditorStyles.boldLabel);
            devID = EditorGUILayout.TextField("Dev ID", devID);
            apiKey = EditorGUILayout.TextField("API Key", apiKey);
            // refreshToken = EditorGUILayout.TextField("Refresh Token", refreshToken);
            if (GUILayout.Button("Login"))
            {
                PerformLoginRequest();
            }
            if(error != "")
            {
                GUILayout.Label("Oops!" + error, EditorStyles.boldLabel);
            }
        }
        else if (state == "load_assets")
        {
            GUILayout.Label("Successful Login", EditorStyles.boldLabel);
            if (GUILayout.Button("Load assets"))
            {
                PerformFetchAssetRequest();
            }
            if (GUILayout.Button("Upload asset page"))
            {
                state = "upload_asset";
            }
        }
        else if (state == "asset_library")
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Logout"))
            {
                Logout();
            }
            if (GUILayout.Button("Refresh assets"))
            {
                PerformFetchAssetRequest();
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Upload asset page"))
            {
                state = "upload_asset";
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            foreach (Asset asset in assets)
            {
                string name = "Asset";
                if (asset.image_tags.Count > 0)
                    name = asset.image_tags[0];
                var originalColor = GUI.color;

                // Check if the current asset is the selected one. If it is, change the button color.
                if (asset == selectedAsset)
                {
                    GUI.color = Color.blue; // Or any color you want for the selected button.
                }
                if (GUILayout.Button(new GUIContent(name)))
                {
                    selectedAsset = asset;
                }
                GUI.color = originalColor;
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Import asset", GUILayout.Width(150)))
            {
                StartEditorCoroutine(DownloadOBJ());
            }
            EditorGUILayout.EndHorizontal();
        }
        else if (state == "upload_asset")
        {
            if (GUILayout.Button("Asset Library Page"))
            {
                state = "load_assets";
            }

            // Add the "Upload Image" button
            GUILayout.Label("Enter your preferences", EditorStyles.boldLabel);
            quality = EditorGUILayout.TextField("Quality", quality);
            polycount = EditorGUILayout.TextField("Polycount", polycount);
            if (GUILayout.Button("Select Image"))
            {
                imagePath = EditorUtility.OpenFilePanel("Select an Image", "", "jpg,png");
                if (!string.IsNullOrEmpty(imagePath))
                {
                    selectedImage = LoadImage(imagePath);
                }
            }

            if (selectedImage != null)
            {
                GUILayout.Label(selectedImage, GUILayout.Width(selectedImage.width), GUILayout.Height(selectedImage.height));
                GUILayout.Label("Selected Image: " + imagePath);
            }

            if (GUILayout.Button("Upload Image"))
            {
                StartEditorCoroutine(UploadImage());
            }

            if(uploadError != "")
            {
                GUILayout.Label("Oops!" + uploadError, EditorStyles.boldLabel);
            }
        }

        
    }

    private void StartEditorCoroutine(IEnumerator routine)
    {
        var coroutine = new EditorCoroutine(routine);
        _coroutines.Add(coroutine);
        EditorApplication.update += UpdateEditorCoroutine;
    }

    private void UpdateEditorCoroutine()
    {
        for (int i = 0; i < _coroutines.Count; i++)
        {
            if (!_coroutines[i].Routine.MoveNext())
            {
                _coroutines[i].MarkAsDone();
            }
        }

        _coroutines.RemoveAll(c => c.IsDone);

        if (_coroutines.Count == 0)
        {
            EditorApplication.update -= UpdateEditorCoroutine;
        }
    }

    bool tryLoginRequest(string destName)
    {
        string url = domain + "/api/v1/registerHook";
        string json = "{ \"devID\": \"" + devID + "\", \"destination\": \"" + destName + "\" }";
        var request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("X-API-Key", apiKey);

        // Send the request
        var operation = request.SendWebRequest();

        // Wait for the request to complete (synchronously)
        while (!operation.isDone)
        {
            Thread.Sleep(100);
        }

        if (request.result == UnityWebRequest.Result.Success)
        {
            error = "";
            string responseData = request.downloadHandler.text;
            ResponseData response = JsonUtility.FromJson<ResponseData>(responseData);

            Debug.Log("Data received: " + responseData);

            if (response.message == "Webhook already registered") return false;

            jwt = response.jwt;
            Debug.Log("JWT: " + jwt);
            EditorPrefs.SetString("DevID", devID);
            EditorPrefs.SetString("APIKey", apiKey);

            state = "load_assets";
            error = "";
            return true;
        }
        else
        {
            error = "incorrect credentials";
            Debug.Log("Request error: " + request.error);
            return false;
        }
    }

    void PerformLoginRequest()
    {
        EditorApplication.update -= PerformLoginRequest; // Remove this function from the update delegate since it only needs to run once

        string destName = "http://example.com/invalid-webhook";
        int maxAttempts = 3;
        int attempts = 0;
        while (attempts < maxAttempts)
        {
            if (tryLoginRequest(destName))
            {
                Debug.Log("Registration successful on attempt " + (attempts+1));
                break;
            }
            destName += "a";
            attempts ++;
        }
        if (attempts == maxAttempts)
        {
            error = "Error trying to login, check credentials";
        }
    }

    void Logout()
    {
        EditorPrefs.SetString("DevID", "");
        EditorPrefs.SetString("APIKey", "");
        EditorPrefs.SetString("RefreshToken", "");
        EditorPrefs.SetString("jwt", "");
        devID = "";
        apiKey = "";
        jwt = "";
        refreshToken = "";
        state = "login";
        imagePath = "";
        selectedImage = null;
        quality = "";
        polycount = "";
    }
    void PerformJwtRequest()
    {
        EditorApplication.update -= PerformJwtRequest; // Remove this function from the update delegate since it only needs to run once

        string url = domain + "/api/v1/refreshJWT";
        string json = "{ \"devID\": \"" + devID + "\"}";
        var request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("X-API-Key", apiKey);
        request.SetRequestHeader("refresh-token", refreshToken);

        // Send the request
        var operation = request.SendWebRequest();

        // Wait for the request to complete (synchronously)
        while (!operation.isDone)
        {
            Thread.Sleep(100);
        }

        if (request.result == UnityWebRequest.Result.Success)
        {
            string response = request.downloadHandler.text; // Get the JSON string from the response
            ResponseData data = JsonUtility.FromJson<ResponseData>(response); // Deserialize it
            jwt = data.jwt;
            EditorPrefs.SetString("RefreshToken", refreshToken);
            EditorPrefs.SetString("jwt", jwt);
            state = "load_assets";
            error = "";
        }
        else
        {
            error = "incorrect credentials";
            Debug.Log("Request error: " + request.downloadHandler.text);
        }
    }

    void PerformFetchAssetRequest()
    {
        EditorApplication.update -= PerformFetchAssetRequest; // Remove this function from the update delegate since it only needs to run once

        string url = domain + "/api/v1/fetchAll";
        string json = "{ \"devID\": \"" + devID + "\"}";
        var request = new UnityWebRequest(url, "GET");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("X-API-Key", apiKey);
        request.SetRequestHeader("Authorization", jwt);

        // Send the request
        var operation = request.SendWebRequest();

        // Wait for the request to complete (synchronously)
        while (!operation.isDone)
        {
            Thread.Sleep(100);
        }

        if (request.result == UnityWebRequest.Result.Success)
        {
            var data = JsonUtility.FromJson<AssetList>(request.downloadHandler.text);
            state = "asset_library";
            List<Asset> filteredData = new List<Asset>();
            foreach (Asset asset in data.assets)
            {
                if (asset.image_tags[0] != "" 
                    && asset.iterations[asset.iterations.Count - 1].status == "completed")
                {
                    filteredData.Add(asset);
                }
            }
            assets = filteredData;
        }
        else
        {
            Debug.Log("Request error: " + request.downloadHandler.text);
        }
    }

    IEnumerator DownloadImage(Asset data)
    {
        Debug.Log(data);

        UnityWebRequest www = UnityWebRequestTexture.GetTexture(data.image[0]);
        yield return www.SendWebRequest();
        while (www.result == UnityWebRequest.Result.InProgress)
        {
            Thread.Sleep(100);
        }

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.Log(www.error);
        }
        else
        {
            data.texture = ((DownloadHandlerTexture)www.downloadHandler).texture;
        }
    }

    IEnumerator DownloadOBJ()
    {
        int len = selectedAsset.iterations.Count;
        string url = selectedAsset.iterations[len - 1].results.obj;
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            yield return www.SendWebRequest();
            while (www.result == UnityWebRequest.Result.InProgress)
            {
                Thread.Sleep(100);
            }
            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.Log(www.error);
            }
            else
            {
                // Or retrieve results as binary data
                byte[] results = www.downloadHandler.data;

                File.WriteAllBytes(Application.dataPath + "/" + selectedAsset.requestID + ".obj", results);
                AssetDatabase.Refresh();
            }
        }
    }

    Texture2D LoadImage(string path)
    {
        byte[] imageData = System.IO.File.ReadAllBytes(path);
        Texture2D texture = new Texture2D(2, 2);
        texture.LoadImage(imageData);
        return texture;
    }

    IEnumerator UploadImage()
    {
        string[] allowedQualityValues = new[] { "standard", "high", "ultra" };

        if (imagePath == "")
        {
            uploadError = "Must Select an Image";
            yield break;
        }
        
        if (!Array.Exists(allowedQualityValues, q => q.ToLower() == quality.ToLower()))
        {
            uploadError = "Invalid quality value. Allowed values are 'standard', 'high', and 'ultra'";
            yield break;
        }

        if (int.TryParse(polycount, out int pcount))
        {
            if (pcount <= 0 && pcount > 30000)
            {
                uploadError = "Invalid Polycount value, must be less than 30000";
                yield break;
            }
        }

        byte[] imageBytes = System.IO.File.ReadAllBytes(imagePath);
        Debug.Log("Image bytes length: " + imageBytes.Length);

        string url = domain + "/api/v1/process";
        string fileName = System.IO.Path.GetFileName(imagePath);

        List<IMultipartFormSection> formData = new List<IMultipartFormSection>();
        formData.Add(new MultipartFormDataSection("devID", "1483c860-1d71-4a90-aea9-ca8ec4a24d48"));
        formData.Add(new MultipartFormFileSection("image", imageBytes, "image.png", "image/png"));
        formData.Add(new MultipartFormDataSection("LoQ", quality));
        formData.Add(new MultipartFormDataSection("polycount", polycount));


        Debug.Log("about to send request");

        UnityWebRequest www = UnityWebRequest.Post(url, formData);
        www.SetRequestHeader("X-API-Key", apiKey);
        www.SetRequestHeader("Authorization", jwt);

        yield return www.SendWebRequest();

        while (www.result == UnityWebRequest.Result.InProgress)
        {
            Thread.Sleep(100);
        }
        if (www.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Image uploaded successfully");
            // Reset any necessary variables or display messages
        }
        else
        {
            Debug.LogError("Failed to upload image: " + www.error);
            Debug.Log("Response: " + www.downloadHandler.text);
            Debug.Log("Response Code: " + www.responseCode);
            // Display error message or handle the failure
        }
        uploadError = "";
        quality = "";
        polycount = "";
        selectedImage = null;
        imagePath = "";

    }
}

