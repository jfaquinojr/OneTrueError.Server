﻿using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization.Formatters;
using Griffin.Data;
using Griffin.Data.Mapper;
using Newtonsoft.Json;

namespace OneTrueError.Infrastructure.Queueing.Ado
{
    public class AdoNetMessageQueue : IMessageQueue
    {
        private readonly string _connectionString;

        private readonly AdoNetQueueEntryMapper _mapper;
        private readonly string _providerName;
        private readonly string _queueName;

        private readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            TypeNameHandling = TypeNameHandling.Auto,
            TypeNameAssemblyFormat = FormatterAssemblyStyle.Simple,
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            ContractResolver = new IncludeNonPublicMembersContractResolver()
        };

        public AdoNetMessageQueue(string queueName, string providerName, string connectionString)
        {
            if (queueName == null) throw new ArgumentNullException(nameof(queueName));
            if (providerName == null) throw new ArgumentNullException(nameof(providerName));
            if (connectionString == null) throw new ArgumentNullException(nameof(connectionString));
            if (string.IsNullOrEmpty(queueName))
                throw new ArgumentNullException("queueName");

            _queueName = queueName;
            _providerName = providerName;
            _connectionString = connectionString;
            _mapper = new AdoNetQueueEntryMapper();
        }

        [SuppressMessage("Microsoft.Security", "CA2100:Review SQL queries for security vulnerabilities")]
        public void Write(int applicationId, object message)
        {
            var json = JsonConvert.SerializeObject(message, _settings);
            using (var connection = OpenConnection())
            {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO Queue" + _queueName +
                                      @" (CreatedAtUtc, ApplicationId, AssemblyQualifiedTypeName, Body) 
                                        VALUES(@CreatedAtUtc, @ApplicationId, @AssemblyQualifiedTypeName, @Body)";
                    cmd.AddParameter("CreatedAtUtc", DateTime.UtcNow);
                    cmd.AddParameter("ApplicationId", applicationId);
                    cmd.AddParameter("AssemblyQualifiedTypeName",
                        message.GetType().FullName + ", " + message.GetType().Assembly.GetName().Name);
                    cmd.AddParameter("body", json);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public IQueueTransaction BeginTransaction()
        {
            var con = OpenConnection();
            return new AdoNetTransaction(con);
        }

        public T Receive<T>()
        {
            using (var con = OpenConnection())
            {
                AdoNetQueueEntry row;
                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = @"SELECT TOP(1) * FROM Queue" + _queueName;
                    row = cmd.FirstOrDefault(_mapper);
                }

                if (row == null)
                    return default(T);

                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = @"DELETE FROM Queue" + _queueName + " WHERE Id = @id";
                    cmd.AddParameter("id", row.Id);
                    cmd.ExecuteNonQuery();
                }

                return JsonConvert.DeserializeObject<T>(row.Body, _settings);
            }
        }

        public object Receive()
        {
            using (var con = OpenConnection())
            {
                AdoNetQueueEntry row;
                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = @"SELECT TOP(1) * FROM Queue" + _queueName;
                    row = cmd.FirstOrDefault(_mapper);
                }

                if (row == null)
                    return null;

                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = @"DELETE FROM Queue" + _queueName + " WHERE Id = @id";
                    cmd.AddParameter("id", row.Id);
                    cmd.ExecuteNonQuery();
                }


                var type = Type.GetType(row.AssemblyQualifiedTypeName);
                if (type == null)
                    throw new InvalidOperationException("Could not build a type from '" + row.AssemblyQualifiedTypeName +
                                                        "'.");
                var body = JsonConvert.DeserializeObject(row.Body, type, _settings);
                return body;
            }
        }

        public T TryReceive<T>(IQueueTransaction transaction, TimeSpan waitTimeout)
        {
            return Receive<T>();
        }

        public T Receive<T>(IQueueTransaction transaction)
        {
            var trans = ((AdoNetTransaction) transaction).Transaction;
            AdoNetQueueEntry row;
            using (var cmd = trans.Connection.CreateCommand())
            {
                cmd.Transaction = trans;
                cmd.CommandText = @"SELECT TOP(1) * FROM Queue" + _queueName;
                row = cmd.FirstOrDefault(_mapper);
            }

            if (row == null)
                return default(T);

            using (var cmd = trans.Connection.CreateCommand())
            {
                cmd.Transaction = trans;
                cmd.CommandText = @"DELETE FROM Queue" + _queueName + " WHERE Id = @id";
                cmd.AddParameter("id", row.Id);
                cmd.ExecuteNonQuery();
            }
            return JsonConvert.DeserializeObject<T>(row.Body);
        }

        public T TryReceive<T>(TimeSpan waitTimeout)
        {
            return Receive<T>();
        }

        private IDbConnection OpenConnection()
        {
            var connectionFactory = DbProviderFactories.GetFactory(_providerName);
            var con = connectionFactory.CreateConnection();
            con.ConnectionString = _connectionString;
            con.Open();
            return con;
        }
    }
}