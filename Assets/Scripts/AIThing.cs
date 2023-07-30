using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Cinemachine;
using DialogueAI;
using Newtonsoft.Json;
using UnityEngine;
using Random = System.Random;
using OpenAI;
using TMPro;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

#pragma warning disable CS4014

public class AIThing : MonoBehaviour
{
    private Random _random = new Random();

    //should probably change this to a safer method of storing the keys
    [SerializeField] private string openAIKey;
    [SerializeField] private string fakeYouUsernameOrEMail;
    [SerializeField] private string fakeYouPassword;

    [SerializeField] private AudioSource audioSource;

    [SerializeField] private CinemachineVirtualCamera _cinemachineVirtualCamera;
    [SerializeField] private TextMeshProUGUI subtitles;
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private List<VideoClip> _clips;

    private HttpClient _client = new();
    private OpenAIApi _openAI;
    private Queue<Dialogue> _dialogues = new();
    private bool _finished = false;
    private string _cookie = "";

    // Start is called before the first frame update
    async void Start()
    {
        _openAI = new OpenAIApi(openAIKey);
        

        if (!File.Exists($"{Environment.CurrentDirectory}\\Assets\\Scripts\\key.txt"))
            File.WriteAllText($"{Environment.CurrentDirectory}\\Assets\\Scripts\\key.txt", "");

        _cookie = File.ReadAllText($"{Environment.CurrentDirectory}\\Assets\\Scripts\\key.txt");

        if (_cookie == "")
        {
            File.Delete($"{Environment.CurrentDirectory}\\Assets\\Scripts\\key.txt");
            File.WriteAllText($"{Environment.CurrentDirectory}\\Assets\\Scripts\\key.txt", "");
            var obj =
                    new
                    {
                        username_or_email = fakeYouUsernameOrEMail,
                        password = fakeYouPassword
                    };

                var response = await _client.PostAsync("https://api.fakeyou.com/login",
                    new StringContent(JsonConvert.SerializeObject(obj), Encoding.UTF8, "application/json"));
                var d = JsonConvert.SerializeObject(response.Headers.GetValues("set-cookie").First());
                var l = d.Split(';');
                _cookie = l[0];
                _cookie = _cookie.Replace("\"", "");
                File.WriteAllText($"{Environment.CurrentDirectory}\\Assets\\Scripts\\key.txt", _cookie);
        }

        // pick a random topic from the topics.json file
         List<string> topics =
             JsonConvert.DeserializeObject<List<string>>(
                 File.ReadAllText($"{Environment.CurrentDirectory}\\Assets\\Scripts\\topics.json"));
         string topic = topics[_random.Next(0, topics.Count)];
        
         //play the timecards/intro
         if (_clips.Count > 0)
         {
             videoPlayer.clip = _clips[_random.Next(0, _clips.Count)];
             videoPlayer.Play();
             StartCoroutine(WaitForTransition(topic));
         }
         else
         {
             Generate(topic);
         }
    }


    private IEnumerator WaitForTransition(string topic)
    {
        while (!videoPlayer.isPlaying) yield return null;
        UnityWebRequest uwr = new UnityWebRequest("https://api.fakeyou.com/v1/billing/active_subscriptions", "GET");
        uwr.SetRequestHeader("Cookie", _cookie);
        uwr.downloadHandler = new DownloadHandlerBuffer();
        yield return uwr.SendWebRequest();
        string checkString = uwr.downloadHandler.text;
        Debug.Log(checkString);
        uwr.Dispose();

        Generate(topic);
    }
    

    async void Generate(string topic)
    {
        string[] text = new[] {""};
        if (File.Exists("Assets/Scripts/Next.txt"))
            text = File.ReadAllLines("Assets/Scripts/Next.txt");

        //delete the script from the file so you don't get the same script twice
        File.WriteAllText("Assets/Scripts/Next.txt", "");

        if (text.Length == 0)
        {
            await GenerateNext(topic);
            text = File.ReadAllLines("Assets/Scripts/Next.txt");
            List<string> topics =
                JsonConvert.DeserializeObject<List<string>>(
                    File.ReadAllText($"{Environment.CurrentDirectory}\\Assets\\Scripts\\topics.json"));
            string t = topics[_random.Next(0, topics.Count)];
            GenerateNext(t);
        }
        else
        {
            GenerateNext(topic);
        }

        StartCoroutine(DoPost(text));
    }

    private IEnumerator DoPost(string[] text)
    {
        StartCoroutine(Speak());
        foreach (var line in text)
        {
            string voicemodelUuid = "";
            string textToSay = "";
            string character = "";
            
            // if line starts with character here
            if (line.StartsWith("SpongeBob:"))
            {
                textToSay = line.Replace("SpongeBob:", "");
                voicemodelUuid = "TM:618j8qwddnsn";
                character = "spongebob";
            }
            else if (line.StartsWith("Spongebob:"))
            {
                textToSay = line.Replace("Spongebob:", "");
                voicemodelUuid = "TM:618j8qwddnsn";
                character = "spongebob";
            }
            else if (line.StartsWith("Patrick:"))
            {
                textToSay = line.Replace("Patrick:", "");
                voicemodelUuid = "TM:ptcaavcfhwxd";
                character = "patrick";
            }
            else if (line.StartsWith("Mr. Krabs"))
            {
                voicemodelUuid = "TM:ade4ta7rc720";
                textToSay = line.Replace("Mr. Krabs", "");
                character = "mrkrabs";
            }
            else if (line.StartsWith("Squidward:"))
            {
                voicemodelUuid = "TM:4e2xqpwqaggr";
                textToSay = line.Replace("Squidward:", "").ToUpper(); //converting to caps because funny loudward
                character = "squidward";
            }
            else if (line.StartsWith("Sandy:"))
            {
                voicemodelUuid = "TM:eaachm5yecgz";
                textToSay = line.Replace("Sandy:", "");
                character = "sandy";
            }


            if (textToSay == "")
                continue;
            
            while (textToSay.Replace(" ", "").Length < 3)
                textToSay += ".";
            

            var jsonObj =
                new
                {
                    inference_text = textToSay,
                    tts_model_token = voicemodelUuid,
                    uuid_idempotency_token = Guid.NewGuid().ToString()
                };
            bool retry = true;
            while (retry)
            {
                UnityWebRequest uwr = new UnityWebRequest("https://api.fakeyou.com/tts/inference", "POST");
                uwr.SetRequestHeader("Cookie", _cookie);
                uwr.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(jsonObj)));
                uwr.downloadHandler = new DownloadHandlerBuffer();
                uwr.SetRequestHeader("Accept", "application/json");
                uwr.SetRequestHeader("Content-Type", "application/json");
                yield return uwr.SendWebRequest();
                string responseString = uwr.downloadHandler.text;
                
                Debug.Log(responseString);
                if (responseString.Contains("too short"))
                    break;
                SpeakResponse speakResponse = JsonConvert.DeserializeObject<SpeakResponse>(responseString);
                
                uwr.Dispose();
                if (!speakResponse.success)
                {
                    yield return new WaitForSecondsRealtime(1.5f);
                    continue;
                }

                retry = false;
                
                _dialogues.Enqueue(new Dialogue
                {
                    uuid = speakResponse.inference_job_token,
                    text = textToSay,
                    character = character,
                    model = voicemodelUuid,
                });
                yield return new WaitForSecondsRealtime(1.5f);
            }
        }
        _finished = true;
    }

    private async Task GenerateNext(string topic)
    {
        try
        {
            //change prompt to whatever you want
            var request = new CreateCompletionRequest
            {
                Model = "text-davinci-003",
                Prompt = $"Create a script for a scene from Spongebob where characters discuss a topic. Possible Characters Include Spongebob, Patrick, Squidward, Sandy, Mr. Krabs, Larry The Lobster and very rarely Gary, Plankton and Mrs. Puff. Use the format: Character: <dialogue>. Only reply with character dialogue. Around 10-14 lines of dialogue with talking only. The topic is: {topic}",
                MaxTokens = 350,
            };
            var response = await _openAI.CreateCompletion(request);
            if (response.Error != null || response.Choices == null)
            {
                GenerateNext(topic);
            }
            else
            {
                var text = response.Choices[0].Text;
                File.WriteAllText("Assets/Scripts/Next.txt", text);
            }
        }
        catch (Exception e)
        {
            Debug.Log(e);
            GenerateNext(topic);
        }
    }

    private IEnumerator Speak()
    {
        while (videoPlayer.isPlaying) yield return null;
        videoPlayer.gameObject.SetActive(false);
        while(_dialogues.Count == 0 && !_finished)
            yield return null;
        if (_dialogues.Count > 0)
        {
            _idleTime = Time.deltaTime;
            yield return Speak(_dialogues.Dequeue());
            StartCoroutine(Speak());
        }
        else if(_finished)
        {
            StartCoroutine(LoadNext());
        }
    }


    private IEnumerator LoadNext()
    {
        //wait for GenerateNext to finish
        while (File.ReadAllText("Assets/Scripts/Next.txt") == "")
        {
            yield return null;
        }

        string s = File.ReadAllText("Assets/Scripts/Next.txt");

        //loads scene based on characters in the script
        //change scene names and if statements to match your shit
        SceneManager.LoadScene("All");
    }

    private IEnumerator Speak(Dialogue d)
    {
        UnityWebRequest uwr = new UnityWebRequest($"https://api.fakeyou.com/tts/job/{d.uuid}", "GET");
        uwr.SetRequestHeader("Cookie", _cookie);
        
        uwr.downloadHandler = new DownloadHandlerBuffer();
        yield return uwr.SendWebRequest();
        string responseString = uwr.downloadHandler.text;
        uwr.Dispose();
        GetResponse v = JsonConvert.DeserializeObject<GetResponse>(responseString);

        if (v.state == null || v.state.status == "pending" || v.state.status == "started")
        {
            yield return new WaitForSeconds(1f); // for rate limiting
            yield return Speak(d);
        }
        else if (v.state.status == "complete_success")
        {
            if (GameObject.Find(d.character) != null && _cinemachineVirtualCamera != null)
            {
                Transform t = GameObject.Find(d.character).transform;
                _cinemachineVirtualCamera.LookAt = t;
                _cinemachineVirtualCamera.Follow = t;
            }
            
            if (subtitles != null)
                subtitles.text = d.text;

            using (var uwr2 = UnityWebRequestMultimedia.GetAudioClip($"https://storage.googleapis.com/vocodes-public{v.state.maybe_public_bucket_wav_audio_path}",
                AudioType.WAV)) //https://docs.unity3d.com/ScriptReference/Networking.UnityWebRequestMultimedia.GetAudioClip.html
            {
                yield return uwr2.SendWebRequest();
                if (uwr2.result == UnityWebRequest.Result.ConnectionError)
                {
                    Debug.Log(uwr.error);
                }
                else
                {
                    audioSource.clip = DownloadHandlerAudioClip.GetContent(uwr2);
                    //funny loudward
                    if (d.character == "squidward")
                    {
                        float[] clipData = new float[audioSource.clip.samples * audioSource.clip.channels];
                        audioSource.clip.GetData(clipData, 0);
                        for (int i = 0; i < clipData.Length; i++)
                        {
                            clipData[i] *= 2.0f;
                        }

                        audioSource.clip.SetData(clipData, 0);
                    }
                    uwr2.Dispose();
                    audioSource.Play();
                    while (audioSource.isPlaying)
                        yield return null;
                }
        }
        }
        else
        {
            var jsonObj =
                new
                {
                    inference_text = d.text,
                    tts_model_token = d.model,
                    uuid_idempotency_token = Guid.NewGuid().ToString()
                };
            var newContent = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(jsonObj));
            bool retry = true;
            while (retry)
            {
                UnityWebRequest uwr3 = new UnityWebRequest("https://api.fakeyou.com/tts/inference", "POST");
                uwr3.SetRequestHeader("Cookie", _cookie);
                uwr3.uploadHandler = new UploadHandlerRaw(newContent);
                uwr3.downloadHandler = new DownloadHandlerBuffer();
                uwr3.SetRequestHeader("Content-Type", "application/json");
                yield return uwr3.SendWebRequest();
                responseString = uwr3.downloadHandler.text;
                uwr3.Dispose();
                SpeakResponse speakResponse = JsonConvert.DeserializeObject<SpeakResponse>(responseString);
                if (speakResponse.success)
                {
                    d.uuid = speakResponse.inference_job_token;
                    retry = false;
                    StartCoroutine(Speak(d));
                }
                
                yield return new WaitForSecondsRealtime(1f);
            }
        }
    }
}
