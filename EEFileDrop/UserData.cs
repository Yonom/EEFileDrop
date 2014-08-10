namespace EEFileDrop
{
    class UserData
    {
        public string Username { get; set; }
        public int UserId { get; set; }

        public UserData(int userId, string username)
        {
            this.UserId = userId;
            this.Username = username;
        }

        public override string ToString()
        {
            return this.Username;
        }
    }
}
