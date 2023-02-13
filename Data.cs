

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using TAP22_23.AlarmClock.Interface;
using TAP22_23.AuctionSite.Interface;
using ISite = TAP22_23.AuctionSite.Interface.ISite;

namespace Carrega_Daniele
{

    public static class Global
    {
        /// Size of hash.
        public const int HashSize = 20;
        /// Size of salt.
        public const int SaltSize = 16;
        //The minimal length of a string to be a well formed site name
        public const int MinSiteName = 1;
        //The maximal length of a string to be a well formed site name
        public const int MaxSiteName = 128;
        //The minimal length of a string to be a well formed user name
        public const int MinUserName = 3;
        //The minimal length of a string to be a well formed user name
        public const int MaxUserName = 64;
        //The maximal length of a string to be a well formed user name
        public const int MinUserPassword = 4;
        //The minimal length of a string to be an acceptable password
        public const int MinTimeZone = -12;
        //The minimal value acceptable as a time zone
        public const int MaxTimeZone = 12;

    }
    public class MyDbContext : TapDbContext
    {
        //protected string _connectionString;

        internal MyDbContext() { }
        public override int SaveChanges()
        {
            //Throws the exception to simulate concurrent violations in the DB if any is memorized in ToBeThrownBySaveChanges
            try
            {
                return base.SaveChanges();
            }
            catch (SqlException e)
            {
                throw new AuctionSiteUnavailableDbException("Unavailable Db", e);
            }
            catch (DbUpdateException e)
            {
                var sqlException = e.InnerException as SqlException;
                if (sqlException == null) throw new AuctionSiteUnavailableDbException("Missing information from Db", e);
                switch (sqlException.Number)
                {
                    case 2601: throw new AuctionSiteNameAlreadyInUseException("Sql error:2601");
                    default:
                        throw new AuctionSiteUnavailableDbException("Missing information form Db exception", e);
                }
            }

        }


        
        public MyDbContext(string connectionstring): base(new DbContextOptionsBuilder<MyDbContext>().UseSqlServer(connectionstring).Options) {        }
       

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {

            var u = modelBuilder.Entity<User>();
            u.HasOne(user => user.Session).WithOne(session => (User)session.User).HasForeignKey<Session>(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
            u.HasMany(user => user.Selling).WithOne(auction => (User) auction.Seller).HasForeignKey(auction => auction.IdSeller)
                .OnDelete(DeleteBehavior.Cascade);
            u.HasMany(user => user.CurrentlyWinning).WithOne(auction => auction.WinUserHaveBid)
                .HasForeignKey(auction => auction.IdWinUserHaveBid).OnDelete(DeleteBehavior.NoAction);

            var s = modelBuilder.Entity<Session>();
            s.HasOne(session => session.Site).WithMany(site => site.Sessions).HasForeignKey(session => session.SiteId)
                .OnDelete(DeleteBehavior.NoAction);

            var a = modelBuilder.Entity<Auction>();
            a.HasOne(auction => auction.Site).WithMany(site => site.Auctions).HasForeignKey(auction => auction.SiteId)
                .OnDelete(DeleteBehavior.NoAction);
        }


        public DbSet<Site> Sites { get; set; }
        public DbSet<Session> Sessions { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Auction> Auctions { get; set; }

    }







    //----------------------------------------ISITE----------------------------------------
    [Index(nameof(Name), IsUnique = true, Name = "NameUnique")]
    public class Site : ISite
    {
        //The hosting service A Host own a database and uses it to save the data of the sites it manages.
        //Any Host may  create and load its sites
        [Key]
        public int SiteId { get; set; }
        public List<User> Users { get; set; } = new();
        
        public List<Auction> Auctions { get; set; } = new();
        public List<Session> Sessions { get; set; } = new();

        [NotMapped]
        private readonly IAlarmClock _alarmClock;
        [NotMapped]
        private readonly string _connectionString;
        
        [Range(1, int.MaxValue)]
        public int SessionExpirationInSeconds { get; set; }
        [Range(double.Epsilon, double.PositiveInfinity)]
        public double MinimumBidIncrement { get; set; }

        [MinLength(Global.MaxSiteName)]
        [MaxLength(Global.MaxSiteName)]
        public string Name { get; set; }

        [Range(Global.MinTimeZone, Global.MaxTimeZone)]
        public int Timezone { get; set; }

        public Site() { }
        public Site(string name, int timezone, int sesionExpirationInSecond, double minimumBidIncrement, IAlarmClock alarmClock, string connectionStriong)
        {
            
            Name = name;
            Timezone = timezone;
            SessionExpirationInSeconds= sesionExpirationInSecond;
            MinimumBidIncrement = minimumBidIncrement;
            _alarmClock= alarmClock;
            _connectionString= connectionStriong;   
        }
        private void CheckUsernameAndPassword(string username, string password)
        {
            if (username == null) throw new AuctionSiteArgumentNullException("Username cannot be null");
            if (password == null) throw new AuctionSiteArgumentNullException("Password cannot be null");
            if (username.Length < Global.MinUserName || username.Length > Global.MaxUserName)
                throw new AuctionSiteArgumentException($"The username length must be beteween {Global.MinUserName} and {Global.MaxUserName}", nameof(username));
            if (password.Length < Global.MinUserPassword) throw new AuctionSiteArgumentException($"The password length must be at least {Global.MinUserPassword}", nameof(password));
        }

        public IEnumerable<IUser> ToyGetUsers()
        {
            //Yields all the users of the site. In a realistic example, this method would be more complex, using some sort of
            //pagination
            if (CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This site appears to have been deleted");
            var userList = new List<IUser>();
            using (var c = new MyDbContext(_connectionString))
            {
                var users = c.Users.Where(u => u.SiteId == SiteId);
                foreach (var user in users)
                {
                    userList.Add(new User(SiteId, user.Username, user.Password, _connectionString, _alarmClock));
                }
            }

            return userList;
        }

        public IEnumerable<ISession> ToyGetSessions()
        {
            //Yields all the sessions of the site. In a realistic example, this method would be more complex, using some sort of
            //pagination
            if (CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This site appears to have been deleted");
            var sessionList = new List<ISession>();
            using (var c = new MyDbContext(_connectionString))
            {
                var sessions = c.Sessions.Include(s => s.User).Where(s => s.SiteId == SiteId);
                foreach (var session in sessions)
                {
                    sessionList.Add(new Session(SiteId, session.UserId, (User) session.User!, session.DbValidUntil, _connectionString, _alarmClock) { Id=session.Id});
                }
            }

            return sessionList;
        }

        public IEnumerable<IAuction> ToyGetAuctions(bool onlyNotEnded)
        {
            //Yields all the (not yet ended) auctions of the site. In a realistic example, this method would be more complex, using
            //some sort of pagination
            if(CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This site appears to have been deleted");
            var auctionList= new  List<IAuction>();
            List<Auction> auctions;
            using (var c = new MyDbContext(_connectionString))
            {
                auctions = c.Auctions.Include(a => a.Seller).Where(a => a.SiteId == SiteId).ToList();
            }
            if (onlyNotEnded)
            {
                foreach (var auction in auctions)
                {
                    if (auction.EndsOn >= _alarmClock.Now)
                    {
                        using (var c = new MyDbContext(_connectionString))
                        {
                            var session = ToyGetSessions().SingleOrDefault(a => a.User.Username == auction.Seller.Username);
                            var seller = c.Users.Include(a => a.Session).SingleOrDefault(a => a.Username == session.User.Username);
                            auctionList.Add(new Auction(auction.Id, auction.Description, auction.IdSeller, seller!, auction.SiteId, auction.EndsOn, auction.PriceNow, _connectionString, _alarmClock) { PriceNow = auction.PriceNow });
                        }
                    }
                }
            }
            else
            {
                foreach (var auction in auctions)
                {
                    using (var c = new MyDbContext(_connectionString))
                    {
                        var session = ToyGetSessions().SingleOrDefault(a => a.User.Username == auction.Seller.Username);
                        var seller = c.Users.Include(a => a.Session).SingleOrDefault(a => a.Username == session.User.Username);
                        auctionList.Add(new Auction(auction.Id, auction.Description, auction.IdSeller, seller!, auction.SiteId, auction.EndsOn, auction.PriceNow, _connectionString, _alarmClock) { PriceNow = auction.PriceNow });
                    }
                }
            }                
            
            return auctionList;
        }
        public ISession? Login(string username, string password)
        {
            if (CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This site appears to have been deleted");
            CheckUsernameAndPassword(username, password);

            using (var c = new MyDbContext(_connectionString))
            {
                var user = c.Users.Include(u => u.Session).SingleOrDefault(u =>
                    u.Username == username && u.SiteId == SiteId &&
                    u.Password == AuctionSiteUtilities.CreateHash(password));

                if (user == null) return null;
                if (user.SessionId == null)
                {
                    var newSession = new Session(SiteId, user.Id, user, _alarmClock.Now.AddSeconds(SessionExpirationInSeconds), _connectionString, _alarmClock) { Id = user.Id.ToString() };
                    user.SessionId = newSession.Id;
                    c.Sessions.Add(newSession);
                    c.SaveChanges();
                    return newSession;
                }
                else
                {
                    var session = user.Session ?? throw new AuctionSiteUnavailableDbException("Unexpected error");
                    session.ValidUntil = _alarmClock.Now.AddSeconds(SessionExpirationInSeconds);
                    c.Update(session);
                    c.SaveChanges();
                    return new Session(SiteId, user.Id, user, session.DbValidUntil, _connectionString, _alarmClock) { Id = session.Id };
                }
            }
        }
        public void CreateUser(string username, string password)
        {
            if (CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This site appears to have been deleted");
            CheckUsernameAndPassword(username, password);
            User? sameNameUser;
            using (var c = new MyDbContext(_connectionString))
            {
                sameNameUser = c.Users.SingleOrDefault(u => u.Username == username);
            }
            if (sameNameUser != null) throw new AuctionSiteNameAlreadyInUseException(username);

            var newUser = new User(SiteId, username, AuctionSiteUtilities.CreateHash(password), _connectionString, _alarmClock);
            using (var c = new MyDbContext(_connectionString))
            {
                c.Users.Add(newUser);
                try
                {
                    c.SaveChanges();
                }
                catch (AuctionSiteNameAlreadyInUseException e)
                {
                    throw new AuctionSiteNameAlreadyInUseException(username, e.Message, e);
                }
            }
        }
        public void Delete()
        {
            //Disposes of the site and all its associated resources
            if (CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This site appears to have been deleted");
            using (var c = new MyDbContext(_connectionString))
            {
                var item = c.Sites.SingleOrDefault(site => site.Name == Name);
                if (item != null)
                {
                    c.Remove(item);
                    c.SaveChanges();
                }
            }
        }

        public DateTime Now()
        {
            if (CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This site appears to have been deleted");
            return _alarmClock.Now;
        }

        private bool CheckIfDeleted()
        {
            using (var c = new MyDbContext(_connectionString))
            {
                var thisSite = c.Sites.SingleOrDefault(s => s.Name == Name);
                if (thisSite == null) return true;
                return false;
            }
        }

        public void OnRingingEvent()
        {
            using (var c = new MyDbContext(_connectionString))
            {
                var sessionsToClean =
                c.Sessions.Where(s => s.SiteId == SiteId && s.DbValidUntil <= _alarmClock.Now);
                c.Sessions.RemoveRange(sessionsToClean);
                c.SaveChanges();
            }
        }


        public override bool Equals(object? obj)
        {
            var item = obj as Site;

            if (item == null)
            {
                return false;
            }

            return Name.Equals(item.Name);
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }
    }
    //----------------------------------------IUSER--------------------------------------------------
    [Index(nameof(SiteId), nameof(Username), IsUnique = true, Name = "UsernameUnique")]
    public class User : IUser
    { 
        [Key]
        public int Id { get; set; }
        public int SiteId { get; }
        public Site? Site { get; set; }
        
        public Session? Session { get; set; }
        public string? SessionId { get; set; } = null;
        [MinLength(Global.MinUserName)]
        [MaxLength(Global.MaxUserName)]
        public string Username { get; set; }
        [MinLength(Global.MinUserPassword)]
        public Byte[] Password { get; set; }

        [NotMapped]
        private readonly string? _connectionString;
        
        public List<Auction>? CurrentlyWinning { get; set; } = new();
        public List<Auction>? Selling { get; set; } = new();
        
        [NotMapped]
        private readonly IAlarmClock? _alarmClock;
        
        
        public User() { }
        public User(int siteId, string username, Byte[] password , string connectionString, IAlarmClock alarmClock)
        {
            Username= username; 
            Password= password;
            SiteId = siteId;
            _connectionString = connectionString;
            _alarmClock = alarmClock;
        }

        public IEnumerable<IAuction> WonAuctions()
        {

            if (CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This user appears to have been deleted");
            var auctionList = new List<IAuction>();
            using (var c = new MyDbContext(_connectionString))
            {
               
                var wonAuctions = c.Auctions.Where(auction =>
                    auction.WinUserHaveBid!.Username == Username).Include(a => a.Seller).ToList();
                foreach (var auction in wonAuctions)
                {
                    if (auction.EndsOn <= _alarmClock.Now)
                        auctionList.Add(new Auction(auction.Id, auction.Description, auction.IdSeller, auction.Seller!, SiteId,   auction.EndsOn, auction.PriceNow, _connectionString, _alarmClock) { MaximumAmount = auction.MaximumAmount });
                }
            }
            return auctionList;
            //Yields the auctions won by the user.
        }
        public void Delete()
        {
            if (CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This user appears to have been deleted");

            using (var c = new MyDbContext(_connectionString))
            {
                var buyingAuctions = c.Auctions.Where(a => a.IdSeller == Id).ToList();
                foreach (var auction in buyingAuctions)
                {
                    if (auction.EndsOn >= _alarmClock.Now)
                        throw new AuctionSiteInvalidOperationException("This user can't be deleted until he is winning an auction");
                }

                var sellingAuctions = c.Auctions.Where(a => a.IdSeller == Id).ToList();
                foreach (var auction in sellingAuctions)
                {
                    if (auction.EndsOn >= _alarmClock.Now)
                        throw new AuctionSiteInvalidOperationException("This user is currently selling an item, so he can't be deleted");
                }
            }

            using (var c = new MyDbContext(_connectionString))
            {
                var auctions = c.Auctions.Where(auction => auction.IdWinUserHaveBid== Id);
                var item = c.Users.SingleOrDefault(user => user.Username == Username && user.SiteId == SiteId);
                foreach (var auction in auctions)
                {
                    auction.WinUserHaveBid = null;
                    auction.IdWinUserHaveBid = null;
                }
                if (item != null)
                {
                    c.Remove(item);
                    c.SaveChanges();
                }
            }
        }

        private bool CheckIfDeleted()
        {
            using (var c = new MyDbContext(_connectionString))
            {
                var thisUser = c.Users.SingleOrDefault(u => u.SiteId == SiteId && u.Username == Username);
                if (thisUser == null) return true;
                return false;
            }
        }

        public override bool Equals(object? obj)
        {
            var item = obj as User;

            if (item == null)
            {
                return false;
            }

            return Username.Equals(item.Username);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SiteId, Username);
        }
    }



    //-----------------------------------------ISESSION----------------------------------------------------

    public class Session : ISession
    {
        [Key]
        public string Id { get; set; }
        public int UserId { get; set; }

        public Site? Site { get; set; }
        public int SiteId { get; set; }

        public DateTime DbValidUntil { get; set; }

        [NotMapped]
        public DateTime ValidUntil
        {
            get
            {
                using (var c = new MyDbContext(_connectionString))
                {
                    var thisSession = c.Sessions.SingleOrDefault(s => s.Id == Id);
                    if (thisSession == null) return DbValidUntil;
                    return thisSession.DbValidUntil;
                }
            }
            set
            {
                DbValidUntil = value;
            }
        }
        [NotMapped]
        public IUser User { get; set; }

        [NotMapped]
        private readonly string _connectionString;

        [NotMapped] private IAlarmClock _alarmClock;

        private Session() { }

        public Session(int siteId, int userId, User user, DateTime validUntil, string connectionString, IAlarmClock alarmClock)
        {
            Id = userId.ToString();
            _connectionString = connectionString;
            SiteId = siteId;
            UserId = userId;
            ValidUntil = validUntil;
            Id = userId.ToString();
            User = user;
            _alarmClock = alarmClock;
        }

        public IAuction CreateAuction(string description, DateTime endsOn, double startingPrice)
        {
            if (CheckIfDeleted())
                throw new AuctionSiteInvalidOperationException("This session has expired");
            using (var c = new MyDbContext(_connectionString))
            {
                if (_alarmClock.Now > ValidUntil) throw new AuctionSiteInvalidOperationException("This session has expired");
            }
            if (description == null) throw new AuctionSiteArgumentNullException("Description cannot be null");
            if (description == "")
                throw new AuctionSiteArgumentException("The description cannot be empty", nameof(description));
            if (startingPrice < 0)
                throw new AuctionSiteArgumentOutOfRangeException(nameof(startingPrice), startingPrice,
                    "The starting price must be a positive integer");
            if (endsOn < _alarmClock.Now)
                throw new AuctionSiteUnavailableTimeMachineException("endsOn cannot precede the current time");
           
            
            using (var c = new MyDbContext(_connectionString))
            {
                var user = c.Users.SingleOrDefault(u => u.SiteId == SiteId && u.Username == User.Username);
                //c.Users.Remove(user);
                if (user == null) throw new AuctionSiteUnavailableDbException("Unexpected Error");
                var newAuction = new Auction(0, description, user.Id,
                    user,   SiteId,  endsOn, startingPrice, _connectionString, _alarmClock);
                c.Auctions.Add(newAuction);
                
                var site = c.Sites.SingleOrDefault(s => s.SiteId == SiteId);
                ValidUntil = _alarmClock.Now.AddSeconds(site!.SessionExpirationInSeconds);
                var thisSession = c.Sessions.SingleOrDefault(s => s.Id == Id);
                thisSession!.DbValidUntil = _alarmClock.Now.AddSeconds(site!.SessionExpirationInSeconds);
                c.SaveChanges();
                newAuction = c.Auctions.OrderByDescending(x=>x.Id).FirstOrDefault();
                return new Auction(newAuction.Id, description, user.Id,user,SiteId, endsOn, startingPrice, _connectionString, _alarmClock);
            }
        }

        public void Logout()
        {
            if (CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("You have already logged out");
            using (var c = new MyDbContext(_connectionString))
            {
                var thisSession = c.Sessions.SingleOrDefault(s => s.Id == Id);
                if (thisSession != null)
                {
                    c.Remove(thisSession);
                    c.SaveChanges();
                }
            }
        }

        private bool CheckIfDeleted()
        {
            using (var c = new MyDbContext(_connectionString))
            {
                var thisSession = c.Sessions.SingleOrDefault(s => s.Id == Id);
                if (thisSession == null) return true;
                return false;
            }
        }

        public override bool Equals(object? obj)
        {
            var item = obj as Session;

            if (item == null)
            {
                return false;
            }

            return Id.Equals(item.Id);
        }
        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }
    }



    //-------------------------------------------------IAUCTION------------------------------------------------
    public class Auction : IAuction
    {
        [Key]
        public int Id { get; set; }
        public int IdSeller { get; set; }
        
        public IUser Seller { get; set; }
        [NotMapped]
        private readonly string _connectionString;
        [NotMapped]
        private readonly IAlarmClock _alarmClock;
        public string Description { get; set; }
        public int SiteId { get; }
        public Site? Site;
        public int? IdWinUserHaveBid { get; set; }
        
        public  User? WinUserHaveBid { get; set; }
        public DateTime EndsOn { get; set; }
        public  double PriceNow { get; set; }
        public double MaximumAmount { get; set; } = 0;
        public Auction()
        {

        }
        public Auction(int Id,string description,int idSeller, IUser seller,int siteId, DateTime endsOn, double priceNow,string connectionString, IAlarmClock alarmClock)
        {
            this.Id = Id;
            Description = description;  
            EndsOn= endsOn; 
            PriceNow = priceNow;
            Seller=seller;
            IdSeller = idSeller;
            _connectionString = connectionString;   
            _alarmClock = alarmClock;   
            SiteId=siteId;
        }
        public IUser? CurrentWinner()
        {
            //Returns the user, if any, who has submitted the highest bid so far. In case no bids have been offered yet, it returns
            //null.It may also return null in case of closed auction whose winner has been deleted from the site (after the auctionended).
            
            if (CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This auction appears to have been deleted");
            using (var c = new MyDbContext(_connectionString))
            {
                var thisAuction = c.Auctions.Include(a => a.WinUserHaveBid ).SingleOrDefault(a => a.SiteId == SiteId && a.Id == Id);
                if (thisAuction!.IdWinUserHaveBid == null) return null;
                return new User(SiteId, thisAuction.WinUserHaveBid.Username, thisAuction.WinUserHaveBid.Password, _connectionString, _alarmClock) {Id=thisAuction.WinUserHaveBid.Id };//{ Id= thisAuction.WinUserHaveBid.Id,Session=(Session)session, SessionId=session.Id};
                
            }
        }

        public double CurrentPrice()
        {
            //  Returns the current price, which is the lowest amount needed to best the second highest bid if two or more bids have
            //been offered; otherwise, it coincides with the starting price.
            if (CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This auction appears to have been deleted");
            using (var c = new MyDbContext(_connectionString))
            {
                var thisAuction = c.Auctions.SingleOrDefault(a => a.SiteId == SiteId && a.Id == Id);
                if (thisAuction == null) throw new AuctionSiteInvalidOperationException("The auction has been deleted");
                return thisAuction.PriceNow;
            }
        }

        public void Delete()
        {
            //Disposes of the auction and all associated resources, if any
            if (CheckIfDeleted())
                throw new AuctionSiteInvalidOperationException("This auction appears to have been deleted");
            using (var c = new MyDbContext(_connectionString))
            {
                var thisAuction = c.Auctions.SingleOrDefault(a => a.Id == Id && a.SiteId == SiteId);
                if (thisAuction != null)
                {
                    c.Remove(thisAuction);
                    c.SaveChanges();
                }
            }
        }

        public bool Bid(ISession session, double offer)
        {
            //Makes a bid for this auction on behalf of the session owner; only possible for still open auctions.
            if (CheckIfDeleted() || EndsOn < _alarmClock.Now)
                throw new AuctionSiteInvalidOperationException("The auction is over or has been deleted");
            if (offer < 0)
                throw new AuctionSiteArgumentOutOfRangeException(nameof(offer), offer,
                    "offer must be a positive integer");
            if (session == null) throw new AuctionSiteArgumentNullException("The session passed is null");

            // check session
            using (var c = new MyDbContext(_connectionString))
            {
                var checkSession = c.Sessions.SingleOrDefault(s => s.Id == session.Id);
                if (checkSession == null) throw new AuctionSiteArgumentException("The session is not valid");
            }


            User? bidder;
            var user = (User)session.User;
            using (var c = new MyDbContext(_connectionString))
            {
                bidder = c.Users.SingleOrDefault(u => u.Username == user.Username && u.SiteId == user.SiteId);
                if (bidder == null) throw new AuctionSiteInvalidOperationException("The bidder can't be null");
            }
            if (session.ValidUntil < _alarmClock.Now || bidder.Equals(Seller) || bidder.SiteId != SiteId)
                throw new AuctionSiteArgumentException("The session or the buyer are invalid");
            Auction? thisAuction;


            using (var c = new MyDbContext(_connectionString))
            {
                thisAuction = c.Auctions.Include(a => a.WinUserHaveBid).Include(a => a.Site).SingleOrDefault(a => a.SiteId == SiteId && a.Id == Id);
            }
            
            
            
            if (thisAuction == null) throw new AuctionSiteInvalidOperationException("The auction is over or has been deleted");
            if (offer < thisAuction.PriceNow) return false;
            
            if (offer < thisAuction.PriceNow + thisAuction.Site!.MinimumBidIncrement && thisAuction.IdWinUserHaveBid!= null) return false;

           // the bidder is (already)the current winner and offer is lower than the maximum offer increased by
                //minimumBidIncrement
            if (thisAuction.IdWinUserHaveBid == bidder.Id &&
                offer < thisAuction.MaximumAmount + thisAuction.Site!.MinimumBidIncrement) return false;

            //the bidder is not the current winner and offer is lower than the current price
            if (thisAuction.IdWinUserHaveBid != bidder.Id && offer < thisAuction.PriceNow && thisAuction.IdWinUserHaveBid != null ) return false;

            //the bidder is not the current winner and offer is lower than the current price increased by minimumBid←-
            //Increment AND this is not the first bid

            if (thisAuction.IdWinUserHaveBid != bidder.Id &&
                offer < thisAuction.PriceNow + thisAuction.Site!.MinimumBidIncrement &&
                thisAuction.IdWinUserHaveBid !=null ) return false;
            



            using (var c = new MyDbContext(_connectionString))
            {
                var site = c.Sites.SingleOrDefault(s => s.SiteId == SiteId);
                var ss = c.Sessions.SingleOrDefault(s => s.Id == session.Id);
                if (ss == null) throw new AuctionSiteArgumentException("The session is not valid");
                ss.DbValidUntil = _alarmClock.Now.AddSeconds(site!.SessionExpirationInSeconds);
                c.SaveChanges();
            }


            if (thisAuction.IdWinUserHaveBid == null && offer >= thisAuction.PriceNow)
            {
                thisAuction.IdWinUserHaveBid = bidder.Id;
                thisAuction.MaximumAmount = offer;

                using (var c = new MyDbContext(_connectionString))
                {
                    c.Auctions.Update(thisAuction);
                    thisAuction.IdWinUserHaveBid = bidder.Id;
                    c.SaveChanges();
                }

                return true;
            }

            
            
            //if the bidder was already winning this auction, the maximum offer is set to offer , current price and current
               //winner are unchanged;

            if (bidder.Equals(thisAuction.WinUserHaveBid))
            {
                thisAuction.MaximumAmount = offer;
                using (var c = new MyDbContext(_connectionString))
                {
                    c.Auctions.Update(thisAuction);
                    c.SaveChanges();
                }

                return true;
            }


            if (thisAuction.MaximumAmount != 0 && !bidder.Equals(thisAuction.WinUserHaveBid) &&
                offer > thisAuction.MaximumAmount)
            {
                double min;
                if (offer < thisAuction.MaximumAmount + thisAuction.Site!.MinimumBidIncrement) min = offer;
                else
                {
                    min = thisAuction.MaximumAmount + thisAuction.Site!.MinimumBidIncrement;
                }

                thisAuction.PriceNow = min;
                thisAuction.MaximumAmount = offer;
                thisAuction.IdWinUserHaveBid = bidder.Id;
                using (var c = new MyDbContext(_connectionString))
                {
                    c.Auctions.Update(thisAuction);
                    thisAuction.IdWinUserHaveBid = bidder.Id;
                    c.SaveChanges();
                }

                return true;
            }

            
            


            double minn;
            if (thisAuction.MaximumAmount < offer + thisAuction.Site!.MinimumBidIncrement)
                minn = thisAuction.MaximumAmount;
            else
            {
                minn = offer + thisAuction.Site!.MinimumBidIncrement;
            }

            thisAuction.PriceNow = minn;
            using (var c = new MyDbContext(_connectionString))
            {
                c.Auctions.Update(thisAuction);
                c.SaveChanges();
            }
            
            return true;
            
        }

        private bool CheckIfDeleted()
        {
            using (var c = new MyDbContext(_connectionString))
            {
                var thisAuction = c.Auctions.SingleOrDefault(a => a.Id == Id && a.SiteId == SiteId);
                if (thisAuction == null) return true;
                return false;
            }
        }
        public override bool Equals(object? obj)
        {
            var item = obj as Auction;

            if (item == null)
            {
                return false;
            }

            return SiteId == item.SiteId && Id == item.Id;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(SiteId, Id);
        }

    }



    internal static class AuctionSiteUtilities
    {
        public const int HASH_SIZE = 24; // size in bytes
        private static byte[] SALT_KEY = new byte[] { 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20 };
        public static byte[] CreateHash(string input)
        {
            // Generate the hash
            Rfc2898DeriveBytes pbkdf2 = new Rfc2898DeriveBytes(input, SALT_KEY);
            return pbkdf2.GetBytes(HASH_SIZE);
        }
    }

}
