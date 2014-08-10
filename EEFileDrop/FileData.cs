namespace EEFileDrop
{
    internal class FileData
    {
        public FileData(string name)
        {
            this.Name = name;
        }

        public string Name { get; set; }
        public byte[] Bytes { get; set; }
    }
}