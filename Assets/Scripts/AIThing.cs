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

    // Start is called before the first frame update
    void Start()
    {
        _openAI = new OpenAIApi(openAIKey);

        Init();
    }

    async void Init()
    {
        if (!File.Exists($"{Environment.CurrentDirectory}\\Assets\\Scripts\\key.txt"))
            File.WriteAllText($"{Environment.CurrentDirectory}\\Assets\\Scripts\\key.txt", "");

        string cookie = File.ReadAllText($"{Environment.CurrentDirectory}\\Assets\\Scripts\\key.txt");

        if (cookie == "")
        {
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
            Debug.Log(d);
            cookie = l[0].Replace("session=", "");
            cookie = cookie.Replace("\"", "");
            File.WriteAllText($"{Environment.CurrentDirectory}\\Assets\\Scripts\\key.txt", cookie);
        }

        _client.DefaultRequestHeaders.Add("Authorization", cookie);
        _client.DefaultRequestHeaders.Add("Accept", "application/json");

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
            StartCoroutine(waitForTransition(topic));
        }
        else
        {
            Generate(topic);
        }
    }

    private IEnumerator waitForTransition(string topic)
    {
        while (videoPlayer.isPlaying)
        {
            yield return null;
        }

        Generate(topic);
    }

    async void Generate(string topic)
    {
        string[] text = new[] {""};
        if (File.Exists("Assets/Scripts/Next.txt"))
            text = File.ReadAllLines("Assets/Scripts/Next.txt");

        //delete the script from the file so you don't get the same script twice
        File.WriteAllText("Assets/Scripts/Next.txt", "");
        List<Dialogue> dialogues = new List<Dialogue>();

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

    var jsonObj = new
    {
        inference_text = textToSay,
        tts_model_token = voicemodelUuid,
        uuid_idempotency_token = Guid.NewGuid().ToString()
    };
    var content = new StringContent(JsonConvert.SerializeObject(jsonObj), Encoding.UTF8, "application/json");
    bool retry = true;
    while (retry)
    {
        var response2 = await _client.PostAsync("https://api.fakeyou.com/tts/inference", content);
        var responseString = await response2.Content.ReadAsStringAsync();
        SpeakResponse speakResponse = JsonConvert.DeserializeObject<SpeakResponse>(responseString);
        if (!speakResponse.success)
        {
            continue;
        }

        retry = false;

        dialogues.Add(new Dialogue
        {
            uuid = speakResponse.inference_job_token,
            text = textToSay,
            character = character
        });
        Debug.Log(responseString);
        await Task.Delay(3000); // for rate limiting. rate limit is so fucking annoying that you get limited even with 3 second delay
    }
}

        StartCoroutine(Speak(dialogues));
    }

    private async Task GenerateNext(string topic)
    {
        //change prompt to whatever you want
        var request = new CreateCompletionRequest
        {
            Model = "text-davinci-003",
            Prompt =
                $"write me a King of the Hill episode about {topic}. only write the dialogue. Stan from american dad is here.",
            MaxTokens = 350
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
            
            Debug.Log("GPT Response:\n" + text); 
        }
    }

    private IEnumerator Speak(List<Dialogue> l)
    {
        videoPlayer.gameObject.SetActive(false);
        foreach (var dialogue in l)
        {
            yield return Speak(dialogue);
        }

        //wait for GenerateNext to finish
        while (File.ReadAllText("Assets/Scripts/Next.txt") == "")
        {
            yield return null;
        }

        string s = File.ReadAllText("Assets/Scripts/Next.txt");

        //loads scene based on characters in the script
        //change scene names and if statements to match your shit
        if (s.Contains("Bobby:") && s.Contains("Peggy:") && s.Contains("Stan:"))
            SceneManager.LoadScene("All");
        else if (s.Contains("Stan:"))
        {
            if (s.Contains("Peggy:"))
                SceneManager.LoadScene("StanPeggy");
            else if (s.Contains("Bobby:"))
                SceneManager.LoadScene("StanBobby");
            else
                SceneManager.LoadScene("Stan");
        }
        else if (s.Contains("Peggy:"))
        {
            if (s.Contains("Bobby:"))
                SceneManager.LoadScene("BobbyPeggy");
            else
                SceneManager.LoadScene("Peggy");
        }
        else if (s.Contains("Bobby:"))
        {
            SceneManager.LoadScene("BobbyPeggy");
        }
        else
        {
            SceneManager.LoadScene("Peggy");
        }
    }

    private IEnumerator Speak(Dialogue d)
    {
        var content = _client.GetAsync($"https://api.fakeyou.com/tts/job/{d.uuid}").Result.Content;
        Debug.Log(content.ReadAsStringAsync().Result);
        var v = JsonConvert.DeserializeObject<GetResponse>(content.ReadAsStringAsync().Result);

        if (v.state == null || v.state.status == "pending" || v.state.status == "started" || v.state.status == "attempt_failed")
        {
            yield return new WaitForSeconds(1.5f); // for rate limiting
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

            using (var uwr = UnityWebRequestMultimedia.GetAudioClip($"https://storage.googleapis.com/vocodes-public{v.state.maybe_public_bucket_wav_audio_path}",
                AudioType.WAV)) //https://docs.unity3d.com/ScriptReference/Networking.UnityWebRequestMultimedia.GetAudioClip.html
            {
                yield return uwr.SendWebRequest();
                if (uwr.result == UnityWebRequest.Result.ConnectionError)
                {
                    Debug.Log(uwr.error);
                }
                else
                {
                    audioSource.clip = DownloadHandlerAudioClip.GetContent(uwr);
                    //funny loudward
                    if (d.character == "peggy")
                    {
                        float[] clipData = new float[audioSource.clip.samples * audioSource.clip.channels];
                        audioSource.clip.GetData(clipData, 0);
                        for (int i = 0; i < clipData.Length; i++)
                        {
                            clipData[i] *= 1.5f;
                        }

                        audioSource.clip.SetData(clipData, 0);
                    }

                    audioSource.Play();
                    while (audioSource.isPlaying)
                        yield return null;
                }
            }
        }
        else
        {
            //too lazy to do it rn but add voicemodeluuid to dialogue, and create a new request here, switch the d.uuid to the new token and then do yield Speak(d)
        }
    }
}

