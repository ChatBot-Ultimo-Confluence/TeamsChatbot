namespace ConfluenceChatBot.Models
{
    public class ConfluencePage
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public int Version { get; set; }
        public DateTime LastModified { get; set; }

        public static implicit operator string(ConfluencePage v)
        {
            throw new NotImplementedException();
        }

        //public void Deconstruct(out string title, out string content, out int version)
        //{
        //    title = Title;
        //    content = Content;
        //    version = Version;
        //}
    }
}
