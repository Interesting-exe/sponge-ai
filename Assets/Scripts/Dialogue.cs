namespace DialogueAI
{
    public class Dialogue
    {
        public string uuid { get; set; }
        public string character { get; set; }
        public string text { get; set; }
    }
    
    public class SpeakResponse
    {
        public string uuid { get; set; }
    }

    public class StatusResponse
    {
        public string path { get; set; }
    }
}