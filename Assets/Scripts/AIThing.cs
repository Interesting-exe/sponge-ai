using System;
using System.Buffers.Text;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
using Unity.VisualScripting;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

#pragma warning disable CS4014

public class AIThing : MonoBehaviour
{
    private Random _random = new Random();

    //should probably change this to a safer method of storing the keys
    [SerializeField] private string openAIKey;
    [SerializeField] private string uberDuckSecret;
    [SerializeField] private string uberDuckKey;

    [SerializeField] private AudioSource audioSource;
    
    [SerializeField] private CinemachineVirtualCamera _cinemachineVirtualCamera;
    [SerializeField] private TextMeshProUGUI subtitles;
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private List<VideoClip> _clips;
    private HttpClient _client = new ();
    private OpenAIApi _openAI;

    // Start is called before the first frame update
    void Start()
    {
        _openAI = new OpenAIApi(openAIKey);
        
        _client.DefaultRequestHeaders.Add("Authorization", $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{uberDuckKey}:{uberDuckSecret}"))}");
        
        //pick a random topic from the topics.json file
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
        string[] text = new []{""};
        if(File.Exists("Assets/Scripts/Next.txt"))
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
            //change the following if statements to match the characters you want to use
            if (line.StartsWith("SpongeBob:"))
            {
                textToSay = line.Replace("SpongeBob:", "");
                voicemodelUuid = "2231cbd3-15a5-4571-9299-b58f36062c45";
                character = "spongebob";
            }
            else if (line.StartsWith("Spongebob:"))
            {
                textToSay = line.Replace("Spongebob:", "");
                voicemodelUuid = "2231cbd3-15a5-4571-9299-b58f36062c45";
                character = "spongebob";
            }
            else if (line.StartsWith("Patrick:"))
            {
                textToSay = line.Replace("Patrick:", "");
                voicemodelUuid = "3b2755d1-11e2-4112-b75b-01c47560fb9c";
                character = "patrick";
            }
            else if (line.StartsWith("Mr. Krabs"))
            {
                voicemodelUuid = "8270ecfc-1491-433e-b4c2-26c1accfe3f0";
                textToSay = line.Replace("Mr. Krabs", "");
                character = "mrkrabs";
            }
            else if (line.StartsWith("Squidward:"))
            {
                voicemodelUuid = "42b30e65-f4cb-4962-ac87-06f3671ccbe4";
                textToSay = line.Replace("Squidward:", "").ToUpper(); //converting to caps because funny loudward
                character = "squidward";
            }
            else if (line.StartsWith("Sandy:"))
            {
                voicemodelUuid = "fd030eea-d80f-4125-8af6-5d28ce21eff6";
                textToSay = line.Replace("Sandy:", "");
                character = "sandy";
            }
            if(textToSay == "")
                continue;

            var jsonObj =
                new
                {
                    speech = textToSay,
                    voicemodel_uuid = voicemodelUuid,
                };
            var content = new StringContent(JsonConvert.SerializeObject(jsonObj), Encoding.UTF8, "application/json");
            var response2 = await _client.PostAsync("https://api.uberduck.ai/speak", content);
            var responseString = await response2.Content.ReadAsStringAsync();
            dialogues.Add(new Dialogue
            {
                uuid = JsonConvert.DeserializeObject<SpeakResponse>(responseString).uuid,
                text = textToSay,
                character = character
            });
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
                $"Create a script for a scene from Spongebob where characters discuss a topic. Possible Characters Include Spongebob, Patrick, Squidward, Sandy, Mr. Krabs, Larry The Lobster and very rarely Gary, Plankton and Mrs. Puff. Use the format: Character: <dialogue>. Only reply with character dialogue. Around 10-14 lines of dialogue with talking only. The topic is: {topic}", // improve prompt for ai so it can use more characters (the same prompt as the official ai sponge, it will make the same format ai moments)
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
        if (s.Contains("Mr. Krabs:") && s.Contains("Squidward:") && s.Contains("Sandy:"))
            SceneManager.LoadScene("All");
        else if (s.Contains("Sandy:"))
        {
            if(s.Contains("Squidward:"))
                SceneManager.LoadScene("SandySquidward");
            else if(s.Contains("Mr. Krabs:"))
                SceneManager.LoadScene("SandyMrKrabs");
            else
                SceneManager.LoadScene("Sandy");
        }
        else if (s.Contains("Squidward:"))
        {
            if(s.Contains("Mr. Krabs:"))
                SceneManager.LoadScene("SquidwardMrKrabs");
            else
                SceneManager.LoadScene("Squidward");
        }
        else if (s.Contains("Mr. Krabs:"))
        {
            SceneManager.LoadScene("SquidwardMrKrabs");
        }
        else
        {
            SceneManager.LoadScene("Squidward");
        }
        
    }
    

    private IEnumerator Speak(Dialogue d)
    {
        while (JsonConvert.DeserializeObject<StatusResponse>(_client.GetAsync($"https://api.uberduck.ai/speak-status?uuid={d.uuid}").Result.Content.ReadAsStringAsync().Result).path == null)
        {
            yield return null;
        }

        if (GameObject.Find(d.character) != null && _cinemachineVirtualCamera != null)
        {
            Transform t = GameObject.Find(d.character).transform;
            _cinemachineVirtualCamera.LookAt = t;
            _cinemachineVirtualCamera.Follow = t;
        }
        
        if(subtitles != null)
            subtitles.text = d.text;

        var v = JsonConvert.DeserializeObject<StatusResponse>(_client.GetAsync($"https://api.uberduck.ai/speak-status?uuid={d.uuid}").Result.Content.ReadAsStringAsync().Result);
        
        using (var uwr = UnityWebRequestMultimedia.GetAudioClip(v.path, AudioType.WAV)) //https://docs.unity3d.com/ScriptReference/Networking.UnityWebRequestMultimedia.GetAudioClip.html
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
                if (d.character == "squidward")
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
                while(audioSource.isPlaying)
                    yield return null;
            }
        }
        
    }
}
