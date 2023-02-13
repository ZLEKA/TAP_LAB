using Carrega_Daniele;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TAP22_23.AlarmClock.Interface;
using TAP22_23.AuctionSite.Interface;

namespace Carrega_Daniele
{

    //-----------------------------------------------IHostFactory----------------------------------------------
    public class HostFactory : IHostFactory
    {


        public void CreateHost(string? connectionString)
        {
            //cancella il db esistene e ne inizializza uno nuovo
            //AuctionSiteArgumentNullException If connectionString is null. 
            //AuctionSiteUnavailableDbException If connectionString is (non - null but) malformed, the DB server is not
            //    responding or returns an unexpected error.            
            if (connectionString == null) throw new AuctionSiteArgumentNullException();
            try
            {
                using (var myDb = new MyDbContext(connectionString))
                {
                    myDb.Database.EnsureDeleted();
                    myDb.Database.EnsureCreated();
                }
            }
            catch { throw new AuctionSiteUnavailableDbException(); }
        }


        public IHost LoadHost(string? connectionString, IAlarmClockFactory? alarmClockFactory)
        {
            // var conn = new ConnectStringMemorize(connectionString);
            //Yields the Host managing a group of Sites
            //having their data resident on the same database.
            //Returns A new instance for the host based on that database.
            //Exceptions: AuctionSiteUnavailableDbException The connection string is (non - null but) malformed, the DB server is not
            //responding or returns an unexpected error.
            //AuctionSiteArgumentNullException If connectionString or alarmClockFactory are null.
            if (connectionString == null || alarmClockFactory == null) throw new AuctionSiteArgumentNullException();
            try
            {
                using (var myDb = new MyDbContext(connectionString))
                {

                    if (!myDb.Database.CanConnect()) throw new AuctionSiteUnavailableDbException();
                }
            }
            catch { throw new AuctionSiteUnavailableDbException(); }

            return new Host(connectionString, alarmClockFactory);
        }
    }

    //------------------------------------------------IHOST--------------------------------------
    [NotMapped]
    public class Host : IHost
    {
        private readonly IAlarmClockFactory _alarmClockFactory;
        private readonly string _connectionString;
        public Host() { }

        public Host(string connectionString, IAlarmClockFactory alarmClockFactory)
        {
            _connectionString = connectionString;
            _alarmClockFactory = alarmClockFactory;
        }

        private void CheckSiteName(string name)
        {
            if (name == null) throw new AuctionSiteArgumentNullException("Site name cannot be null");
            if (name.Length < Global.MinSiteName || name.Length > Global.MaxSiteName)
                throw new AuctionSiteArgumentException($"Site name length must be between {Global.MinSiteName} and {Global.MaxSiteName}", nameof(name));
        }

        public IEnumerable<(string Name, int TimeZone)> GetSiteInfos()
        {
            using (var c = new MyDbContext(_connectionString))
            {
                IQueryable<Site> sites;
                try
                {
                    sites = c.Sites.AsQueryable();
                }
                catch (SqlException e)
                {
                    throw new AuctionSiteUnavailableDbException("Unexpected error", e);
                }
                foreach (var site in sites)
                {
                    yield return (site.Name, site.Timezone);
                }
            }
        }
        public void CreateSite(string name, int timezone, int sessionExpirationTimeInSeconds, double minimumBidIncrement)
        {
            CheckSiteName(name);
            if (timezone > Global.MaxTimeZone || timezone < Global.MinTimeZone || minimumBidIncrement < 0 || sessionExpirationTimeInSeconds <= 0) throw new AuctionSiteArgumentOutOfRangeException("-12 <= timezone >= 12");
            if (timezone < Global.MinTimeZone || timezone > Global.MaxTimeZone)
                throw new AuctionSiteArgumentOutOfRangeException(nameof(timezone), timezone,
                    "timezone must be an integer between -12 and 12");
            if (sessionExpirationTimeInSeconds <= 0)
                throw new AuctionSiteArgumentOutOfRangeException(nameof(sessionExpirationTimeInSeconds),
                    sessionExpirationTimeInSeconds, "expiration time must be positive");
            if (minimumBidIncrement < 0)
                throw new AuctionSiteArgumentOutOfRangeException(nameof(minimumBidIncrement),
                    minimumBidIncrement, "minimum bid increment must be positive");
            var newSite = new Site(name, timezone, sessionExpirationTimeInSeconds, minimumBidIncrement,
                _alarmClockFactory.InstantiateAlarmClock(timezone), _connectionString);
            using (var c = new MyDbContext(_connectionString))
            {
                var existingSite = c.Sites.SingleOrDefault(s => s.Name == name);
                if (existingSite != null)
                    throw new AuctionSiteNameAlreadyInUseException(name, "This name is already used for another site");
                c.Sites.Add(newSite);
                try
                {
                    c.SaveChanges();
                }
                catch (AuctionSiteNameAlreadyInUseException e)
                {
                    throw new AuctionSiteNameAlreadyInUseException(name, "This name is already used for another site", e);
                }
            }

        }

        public ISite LoadSite(string name)
        {
            CheckSiteName(name);
            using (var c = new MyDbContext(_connectionString))
            {
                try
                {
                    var site = c.Sites.SingleOrDefault(s => s.Name == name);
                    if (site == null) throw new AuctionSiteInexistentNameException(name, "this site name is nonexistent");
                    var alarmClock = _alarmClockFactory.InstantiateAlarmClock(site.Timezone);
                    var alarm = alarmClock.InstantiateAlarm(300000);
                    var newSite = new Site(site.Name, site.Timezone, site.SessionExpirationInSeconds, site.MinimumBidIncrement, _alarmClockFactory.InstantiateAlarmClock(site.Timezone), _connectionString) { SiteId = site.SiteId };
                    alarm.RingingEvent += newSite.OnRingingEvent;
                    return newSite;
                }
                catch (SqlException e)
                {
                    throw new AuctionSiteUnavailableDbException("Unexpected DB error", e);
                }
            }
        }
    }
}
