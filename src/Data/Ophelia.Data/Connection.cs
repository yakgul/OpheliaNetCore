﻿using Ophelia.Data.Querying;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Ophelia.Data
{
    public class Connection : DbConnection
    {
        public static Dictionary<string, Type> ConnectionProviders { get; private set; } = new Dictionary<string, Type>();
        public QueryLogger Logger { get; set; }
        internal DbConnection InternalConnection;
        private DatabaseType _Type;
        private DataContext Context;
        public DatabaseTransaction? CurrentTransaction { get; private set; }
        public DatabaseType Type
        {
            get
            {
                return this._Type;
            }
            internal set
            {
                this._Type = value;
            }
        }
        public bool CloseAfterExecution { get; set; }

        public override string ConnectionString
        {
            get
            {
                return this.InternalConnection.ConnectionString;
            }
            set
            {
                this.InternalConnection.ConnectionString = value;
            }
        }

        public override string Database
        {
            get
            {
                return this.InternalConnection.Database;
            }
        }

        public override string DataSource
        {
            get
            {
                return this.InternalConnection.DataSource;
            }
        }

        public override string ServerVersion
        {
            get
            {
                return this.InternalConnection.ServerVersion;
            }
        }

        public override ConnectionState State
        {
            get
            {
                return this.InternalConnection.State;
            }
        }

        public override void ChangeDatabase(string databaseName)
        {
            this.InternalConnection.ChangeDatabase(databaseName);
        }

        public override void Close()
        {
            this.InternalConnection.Close();
        }

        public override void Open()
        {
            this.InternalConnection.Open();
        }
        public new DbTransaction BeginTransaction()
        {
            this.CheckConnection();
            this.CloseAfterExecution = false;
            this.CurrentTransaction = DatabaseTransaction.Create(this, IsolationLevel.ReadUncommitted);
            return this.CurrentTransaction;
        }
        public new DbTransaction BeginTransaction(IsolationLevel isolationLevel)
        {
            this.CheckConnection();
            this.CloseAfterExecution = false;
            this.CurrentTransaction = DatabaseTransaction.Create(this, IsolationLevel.ReadUncommitted);
            return this.CurrentTransaction;
        }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            this.CheckConnection();
            this.CloseAfterExecution = false;
            this.CurrentTransaction = DatabaseTransaction.Create(this, IsolationLevel.ReadUncommitted);
            return this.CurrentTransaction;
        }

        protected override DbCommand CreateDbCommand()
        {
            var command = DbProviderFactories.GetFactory(this.InternalConnection).CreateCommand();
            command.Connection = this.InternalConnection;
            this.ValidateCurrentTransaction(command);
            return command;
        }

        protected virtual DbDataAdapter CreateDataAdapter()
        {
            return System.Data.Common.DbProviderFactories.GetFactory(this.InternalConnection).CreateDataAdapter();
        }

        public virtual DbParameter CreateParameter(DbCommand cmd, string name, object value)
        {
            var param = cmd.CreateParameter();
            param.ParameterName = name;
            param.Value = value ?? DBNull.Value;
            return param;
        }

        public virtual DbParameter CreateParameter(DbCommand cmd, string name, object value, DbType type)
        {
            var param = cmd.CreateParameter();
            param.ParameterName = name;
            param.Value = value ?? DBNull.Value;
            param.DbType = type;
            return param;
        }
        private void ValidateCurrentTransaction(DbCommand cmd)
        {
            if (this.CurrentTransaction == null)
                return;

            if (this.CurrentTransaction.Status != DatabaseTransactionStatus.Created && this.CurrentTransaction.Status != DatabaseTransactionStatus.Saved && this.CurrentTransaction.Status != DatabaseTransactionStatus.Released)
                return;

            cmd.Transaction = this.CurrentTransaction.InternalTransaction;
        }
        public Connection(DataContext context, DatabaseType type, string ConnectionString)
        {
            this.Context = context;
            this.Logger = context.CreateLogger();
            this._Type = type;
            switch (this.Type)
            {
                case DatabaseType.SQLServer:
                    this.InternalConnection = (DbConnection)Activator.CreateInstance(ConnectionProviders["SQLServer"], ConnectionString);
                    break;
                case DatabaseType.PostgreSQL:
                    this.InternalConnection = (DbConnection)Activator.CreateInstance(ConnectionProviders["Npgsql"], ConnectionString);
                    break;
                case DatabaseType.Oracle:
                    this.InternalConnection = (DbConnection)Activator.CreateInstance(ConnectionProviders["Oracle"], ConnectionString);
                    this.Context.Configuration.UseNamespaceAsSchema = false;
                    this.Context.Configuration.UseUppercaseObjectNames = true;
                    this.Context.Configuration.ObjectNameCharLimit = 30;
                    break;
                case DatabaseType.MySQL:
                    this.Context.Configuration.UseNamespaceAsSchema = false;
                    this.InternalConnection = (DbConnection)Activator.CreateInstance(ConnectionProviders["MySQL"], ConnectionString);
                    break;
            }

            this.CloseAfterExecution = true;
        }
        private string PreventOracleSemiColonError(string sql)
        {
            if (this.Type == DatabaseType.Oracle && sql.EndsWith(";"))
                sql = sql.Trim(';');
            return sql;
        }
        public string FormatSQL(string sql)
        {
            sql = sql.Replace("[", this.GetOpeningBracket()).Replace("]", this.GetClosingBracket()).Replace("@p", this.GetParameterNameSign() + "p");
            return this.PreventOracleSemiColonError(sql);
        }
        public object ExecuteNonQuery(string sql)
        {
            return this.ExecuteNonQuery(sql, null);
        }

        public object ExecuteNonQuery(string sql, params object[] parameters)
        {
            using (DbCommand cmd = this.CreateCommand())
            {
                var param = new List<DbParameter>();
                if (parameters != null)
                {
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        param.Add(this.CreateParameter(cmd, this.FormatParameterName("p" + i), parameters[i]));
                    }
                }
                return ExecuteNonQuery(cmd, sql, param.ToArray());
            }
        }

        public object ExecuteNonQuery(DbCommand cmd, string sql, DbParameter[] sqlParameters)
        {
            Model.SQLLog log = null;
            try
            {
                if (this.Context.Configuration.LogSQL)
                {
                    log = new Model.SQLLog(sql, sqlParameters);
                    this.Logger.LogSQL(log);
                    log.Start();
                }

                cmd.CommandText = this.PreventOracleSemiColonError(sql);
                if (sqlParameters != null)
                {
                    foreach (var param in sqlParameters)
                        cmd.Parameters.Add(param);
                }
                this.CheckConnection();
                return cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message + " " + sql, ex);
            }
            finally
            {
                if (this.Context.Configuration.LogSQL)
                    log.Finish();

                if (this.CloseAfterExecution)
                    this.Close();
            }
        }

        public object ExecuteScalar(string sql)
        {
            return this.ExecuteScalar(sql, null);
        }

        public object ExecuteScalar(DbCommand cmd, string sql, DbParameter[] sqlParameters)
        {
            Model.SQLLog log = null;
            try
            {
                if (this.Context.Configuration.LogSQL)
                {
                    log = new Model.SQLLog(sql, sqlParameters);
                    this.Logger.LogSQL(log);
                    log.Start();
                }
                cmd.CommandText = this.PreventOracleSemiColonError(sql);
                if (sqlParameters != null)
                {
                    foreach (var param in sqlParameters)
                        cmd.Parameters.Add(param);
                }
                this.CheckConnection();
                return cmd.ExecuteScalar();
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message + " " + sql, ex);
            }
            finally
            {
                if (this.Context.Configuration.LogSQL)
                    log.Finish();
                if (this.CloseAfterExecution)
                    this.Close();
            }
        }

        public object ExecuteScalar(string sql, params object[] parameters)
        {
            using (var cmd = this.CreateCommand())
            {
                var param = new List<DbParameter>();
                if (parameters != null)
                {
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        param.Add(this.CreateParameter(cmd, this.FormatParameterName("p" + i), parameters[i]));
                    }
                }
                return ExecuteScalar(cmd, sql, param.ToArray());
            }
        }

        /// <summary>
        /// Dynamic/Anonuymous typed parameters
        /// </summary>
        /// <param name="sql"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public object ExecuteCommand(string sql, object parameters)
        {
            using (var cmd = this.CreateCommand())
            {
                var param = new List<DbParameter>();
                if (parameters != null)
                {
                    var type = parameters.GetType();
                    var props = type.GetProperties().ToDictionary(op => op.Name, op => op.GetValue(parameters, null));
                    foreach (var item in props)
                    {
                        if (item.Value == null)
                            param.Add(this.CreateParameter(cmd, "@" + item.Key, DBNull.Value));
                        else
                            param.Add(this.CreateParameter(cmd, "@" + item.Key, item.Value));
                    }
                }
                return ExecuteScalar(cmd, sql, param.ToArray());
            }
        }

        public DataTable GetData(string sqlSelect)
        {
            return this.GetData(sqlSelect, null);
        }

        public DataTable GetData(string sqlSelect, params object[] parameters)
        {
            return this.GetData(sqlSelect, 0, 0, parameters);
        }

        public DataTable GetData(string sqlSelect, int startRecord, int maxCount, params object[] parameters)
        {
            using (var cmd = this.CreateCommand())
            {
                var param = new List<DbParameter>();
                if (parameters != null)
                {
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        param.Add(this.CreateParameter(cmd, this.FormatParameterName("p" + i), parameters[i]));
                    }
                }
                return this.GetData(cmd, sqlSelect, startRecord, maxCount, param.ToArray());
            }
        }
        public DataTable GetPagedData(string sqlSelect, int page, int pageSize, params object[] parameters)
        {
            using (var cmd = this.CreateCommand())
            {
                var param = new List<DbParameter>();
                if (parameters != null)
                {
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        param.Add(this.CreateParameter(cmd, this.FormatParameterName("p" + i), parameters[i]));
                    }
                }
                return this.GetData(cmd, sqlSelect, (page - 1) * pageSize, pageSize, param.ToArray());
            }
        }
        public DataTable GetPagedData(DbCommand cmd, string sqlSelect, int page, int pageSize, DbParameter[] sqlParameters)
        {
            return this.GetData(cmd, sqlSelect, (page - 1) * pageSize, pageSize, sqlParameters);
        }
        public DataTable GetData(DbCommand cmd, string sqlSelect, int startRecord, int maxCount, DbParameter[] sqlParameters)
        {
            Model.SQLLog? log = null;
            try
            {
                bool canApplyDBLevelPaging = this.Context.Configuration.UseDBLevelPaging && maxCount > 0 && this.Type != DatabaseType.Oracle && this.Type != DatabaseType.MySQL;
                if (canApplyDBLevelPaging)
                {
                    canApplyDBLevelPaging = !sqlSelect.Contains(" TOP ", StringComparison.InvariantCultureIgnoreCase) &&
                        sqlSelect.IndexOf(" ORDER BY ", StringComparison.InvariantCultureIgnoreCase) > -1 &&
                        !sqlSelect.Contains("ROWS FETCH NEXT", StringComparison.InvariantCultureIgnoreCase);
                    if (canApplyDBLevelPaging)
                        sqlSelect += " OFFSET " + startRecord + " ROWS FETCH NEXT " + maxCount + " ROWS ONLY";
                }

                if (this.Context.Configuration.LogSQL)
                {
                    log = new Model.SQLLog(sqlSelect, sqlParameters);
                    this.Logger.LogSQL(log);
                    log.Start();
                }
                cmd.CommandText = this.PreventOracleSemiColonError(sqlSelect);
                if (sqlParameters != null)
                {
                    foreach (var param in sqlParameters)
                        cmd.Parameters.Add(param);
                }

                var Adapter = this.CreateDataAdapter();
                Adapter.SelectCommand = cmd;
                DataSet DataSet = new System.Data.DataSet();

                this.CheckConnection();
                if (canApplyDBLevelPaging || maxCount == 0)
                    Adapter.Fill(DataSet, "Table1");
                else
                    Adapter.Fill(DataSet, startRecord, maxCount, "Table1");

                return DataSet.Tables[0];
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message + " " + sqlSelect, ex);
            }
            finally
            {
                if (log != null && this.Context.Configuration.LogSQL)
                    log.Finish();
                if (this.CloseAfterExecution)
                    this.Close();
            }
        }
        public object FormatParameterValue(object value, bool isString = false)
        {
            if (value != null && !isString)
            {
                if (value.ToString() == "True" || value.ToString() == "False")
                {
                    if (this.Context.Configuration.QueryBooleanAsBinary)
                        return (value.ToString() == "True" ? 1 : 0);
                    else if (value is bool || value is Nullable<bool>)
                        return value;
                }
                else if (value is DateTime || value is Nullable<DateTime>)
                {
                    DateTime val = DateTime.MinValue;
                    if (value is DateTime)
                        val = (DateTime)value;
                    else if (value is Nullable<DateTime>)
                        val = ((DateTime?)value).GetValueOrDefault(DateTime.MinValue);

                    if (val < this.Context.Configuration.MinDateTime)
                        return this.Context.Configuration.MinDateTime;
                    else if (val > this.Context.Configuration.MaxDateTime)
                        return this.Context.Configuration.MaxDateTime;
                    return value;
                }
            }
            else if (value != null && isString && this.Context.Configuration.StringParameterFormatter != null)
            {
                value = this.Context.Configuration.StringParameterFormatter(value.ToString());
            }
            return value;
        }
        public string FormatParameterName(string Name)
        {
            return this.GetParameterNameSign() + Name;
        }
        private string GetParameterNameSign()
        {
            switch (this.Type)
            {
                case DatabaseType.Oracle:
                    return ":";
                default:
                    return "@";
            }
        }
        public string FormatStringConcat(string Name)
        {
            switch (this.Type)
            {
                case DatabaseType.SQLServer:
                    return Name;
                case DatabaseType.PostgreSQL:
                    return Name.Replace("+", "||");
                case DatabaseType.Oracle:
                    return Name.Replace("+", "||");
                case DatabaseType.MySQL:
                    return Name.Replace("+", "||"); ;
            }
            return "";
        }

        private void CheckConnection()
        {
            if (this.State == ConnectionState.Closed)
            {
                this.Open();
                if (this.Type == DatabaseType.Oracle)
                {
                    var cae = this.CloseAfterExecution;
                    this.CloseAfterExecution = false;
                    this.ExecuteNonQuery("ALTER SESSION SET NLS_SORT=BINARY_CI");
                    this.ExecuteNonQuery("ALTER SESSION SET NLS_COMP=LINGUISTIC");
                    this.CloseAfterExecution = cae;
                }
            }
        }

        public long GetSequenceNextVal(Type type)
        {
            var seqName = this.GetSequenceName(type);
            switch (this.Type)
            {
                case DatabaseType.PostgreSQL:
                    return Convert.ToInt64(this.ExecuteScalar("SELECT nextval('" + seqName + "')"));
                case DatabaseType.Oracle:
                    return Convert.ToInt64(this.ExecuteScalar("SELECT " + seqName + ".nextval FROM DUAL"));
            }
            return 0;
        }

        public long GetSequenceNextVal(Type type, PropertyInfo prop, bool singleAtTable = true)
        {
            if (singleAtTable)
                return this.GetSequenceNextVal(type);

            var seqName = this.GetSequenceName(type, prop.Name);
            try
            {
                switch (this.Type)
                {
                    case DatabaseType.PostgreSQL:
                        return Convert.ToInt64(this.ExecuteScalar("SELECT nextval('" + seqName + "')"));
                    case DatabaseType.Oracle:
                        return Convert.ToInt64(this.ExecuteScalar("SELECT " + seqName + ".nextval FROM DUAL"));
                }
            }
            catch (Exception)
            {
                var designer = new DataDesigner();
                designer.Context = this.Context;
                var sql = designer.CreateSequence(seqName, false);
                if (!string.IsNullOrEmpty(sql))
                {
                    this.ExecuteNonQuery(sql);

                    switch (this.Type)
                    {
                        case DatabaseType.PostgreSQL:
                            return Convert.ToInt64(this.ExecuteScalar("SELECT nextval('" + seqName + "')"));
                        case DatabaseType.Oracle:
                            return Convert.ToInt64(this.ExecuteScalar("SELECT " + seqName + ".nextval FROM DUAL"));
                    }
                }
            }
            return 0;
        }
        public string GetSequenceName(Type type, string suffix = "")
        {
            var tableName = $"{this.GetTableName(type, false)}_{suffix}";
            if (this.Context.Connection.Type == DatabaseType.Oracle)
            {
                if (tableName.Length > 28)
                    return "S_" + tableName.Left(28);
                else
                    return "S_" + tableName;
            }
            return "SEQ_" + tableName;
        }
        public string GetTableName(string schema, string name, bool format = true, string databaseName = "")
        {
            var sb = new StringBuilder();
            if (format)
            {
                if (!string.IsNullOrEmpty(databaseName))
                    sb.Append(this.FormatDataElement(databaseName) + ".");

                if (this.Context.Configuration.UseNamespaceAsSchema)
                {
                    if (!string.IsNullOrEmpty(schema))
                        sb.Append(this.FormatDataElement(this.GetMappedNamespace(schema).Replace(".", "_"))).Append(".").Append(this.FormatDataElement(this.GetMappedTableName(name)));
                    else
                        sb.Append(this.FormatDataElement(this.GetMappedTableName(name)));
                }
                else
                {
                    if (!string.IsNullOrEmpty(schema))
                        sb.Append(this.FormatDataElement(this.GetMappedNamespace(schema).Replace(".", "_") + "_" + this.GetMappedTableName(name)));
                    else
                        sb.Append(this.FormatDataElement(this.GetMappedTableName(name)));
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(databaseName))
                    sb.Append(this.FormatDataElement(databaseName) + ".");
                if (!string.IsNullOrEmpty(schema))
                    sb.Append(this.GetMappedNamespace(schema).Replace(".", "_")).Append("_").Append(this.GetMappedTableName(name));
                else
                    sb.Append(this.GetMappedTableName(name));
            }
            return sb.ToString();
        }
        internal string GetMappedNamespace(string sp)
        {
            if (this.Context.NamespaceMap.ContainsKey(sp))
            {
                sp = this.Context.NamespaceMap[sp];
            }
            if (this.Context.Configuration.UseUppercaseObjectNames)
                sp = sp.ToUpper().Replace("İ", "I");
            return sp;
        }
        internal string GetMappedTableName(string tb)
        {
            if (this.Context.TableMap.ContainsKey(tb))
            {
                tb = this.Context.TableMap[tb];
            }
            if (this.Context.Configuration.UseUppercaseObjectNames)
                tb = tb.ToUpper().Replace("İ", "I");
            return tb;
        }
        internal string GetMappedFieldName(string f)
        {
            if (this.Context.FieldMap.ContainsKey(f))
            {
                f = this.Context.FieldMap[f];
            }
            if (this.Context.Configuration.UseUppercaseObjectNames)
                f = f.ToUpper().Replace("İ", "I");
            return f;
        }
        public string GetTableName(Type type, bool format = true)
        {
            var dbName = "";
            var tableName = type.Name;
            var schema = this.GetSchema(type);

            var tableAttr = (System.ComponentModel.DataAnnotations.Schema.TableAttribute)type.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.TableAttribute)).FirstOrDefault();
            if (tableAttr != null)
            {
                tableName = tableAttr.Name;
                schema = tableAttr.Schema;
            }
            if (this.Context.Configuration.AllowLinkedDatabases)
            {
                if (!this.Context.ContainsEntityType(type))
                {
                    var ctx = this.Context.GetContext(type);
                    if (ctx != null && ctx.Connection.Database != this.Context.Connection.Database)
                        dbName = ctx.Connection.Database;
                }
                else if (this.Context.Configuration.LinkedDatabases != null && this.Context.Configuration.LinkedDatabases.Count > 0)
                {
                    foreach (var ctxType in this.Context.Configuration.LinkedDatabases.Keys)
                    {
                        var entityType = ctxType.Assembly.GetTypes().Where(op => op.FullName == type.FullName).FirstOrDefault();
                        if (entityType != null)
                            dbName = this.Context.Configuration.LinkedDatabases[ctxType];
                    }
                }
            }
            return GetTableName(schema, tableName, format, dbName);
        }
        public string GetSchema(Type type)
        {
            var schema = type.Namespace;
            foreach (var key in this.Context.Configuration.NamespacesToIgnore)
            {
                schema = schema.Replace(key, "").Trim('.');
            }
            return this.GetMappedNamespace(schema);
        }
        public string GetPrimaryKeyName(Type type)
        {
            var pkPRoperty = Extensions.GetPrimaryKeyProperty(type);
            if (this.Context.Configuration.PrimaryKeyContainsEntityName)
                return this.FormatDataElement(this.GetMappedFieldName(type.Name + Extensions.GetColumnName(pkPRoperty)));
            else
                return this.FormatDataElement(this.GetMappedFieldName(Extensions.GetColumnName(pkPRoperty)));
        }

        internal string FormatDataElement(string key)
        {
            return this.GetOpeningBracket() + this.CheckCharLimit(key) + this.GetClosingBracket();
        }
        internal string CheckCharLimit(string key)
        {
            if (this.Context.Configuration.ObjectNameCharLimit > 0 && key.Length > this.Context.Configuration.ObjectNameCharLimit)
            {
                key = key.Left(this.Context.Configuration.ObjectNameCharLimit);
            }
            return key;
        }
        private string GetOpeningBracket()
        {
            switch (this.Type)
            {
                case DatabaseType.SQLServer:
                    return "[";
                case DatabaseType.PostgreSQL:
                    return "\"";
                case DatabaseType.Oracle:
                    return "\"";
                case DatabaseType.MySQL:
                    return "`";
            }
            return "";
        }
        internal void ReleaseCurrentTransaction()
        {
            this.CurrentTransaction = null;
            this.CloseAfterExecution = true;
        }
        private string GetClosingBracket()
        {
            switch (this.Type)
            {
                case DatabaseType.SQLServer:
                    return "]";
                case DatabaseType.PostgreSQL:
                    return "\"";
                case DatabaseType.Oracle:
                    return "\"";
                case DatabaseType.MySQL:
                    return "`";
            }
            return "";
        }
        public PropertyInfo[] GetAllFields(Querying.Query.Helpers.Table table)
        {
            return table.EntityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        }
        public string GetAllSelectFields(Querying.Query.Helpers.Table table, bool isSubTable = true, bool loadByXML = false)
        {
            var sb = new StringBuilder();
            var excludedProps = table.EntityType.GetCustomAttributes(typeof(Attributes.ExcludeDefaultColumn));
            var properties = this.GetAllFields(table);
            foreach (var p in properties)
            {
                if (table.Query.Data.Excluders.Where(op => op.Name == p.Name).Any())
                {
                    continue;
                }
                else
                {
                    if (p.PropertyType.Name != "String" && typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType))
                        continue;
                    if (p.PropertyType.Name != "String" && p.PropertyType.IsClass && !p.PropertyType.IsValueType)
                        continue;
                    if (excludedProps == null || !excludedProps.Where(op => ((Attributes.ExcludeDefaultColumn)op).Columns.Contains(p.Name)).Any())
                    {
                        var pAttributes = p.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute));
                        if (pAttributes == null || !pAttributes.Any())
                        {
                            var fieldStr = this.GetFieldSelectString(table, p, isSubTable, loadByXML);
                            sb.Append(fieldStr);
                            if (!string.IsNullOrEmpty(fieldStr))
                                sb.Append(",");
                        }
                    }
                }
            }

            return sb.ToString().Trim(',');
        }

        public string GetFieldSelectString(Querying.Query.Helpers.Table table, PropertyInfo p, bool isSubTable = true, bool loadByXML = false)
        {
            var sb = new StringBuilder();

            if (!p.DeclaringType.IsAnonymousType())
            {
                if (!p.CanWrite || !p.CanRead) { return ""; }

                MethodInfo mget = p.GetGetMethod(false);
                MethodInfo mset = p.GetSetMethod(false);

                // Get and set methods have to be public
                if (mget == null) { return ""; }
                if (mset == null) { return ""; }
            }

            if (p.PropertyType.IsDataEntity()) { return ""; }
            if (p.PropertyType.IsQueryableDataSet()) { return ""; }

            var fieldName = Extensions.GetColumnName(p);
            var alias = this.FormatDataElement(this.GetMappedFieldName(table.Alias + "_" + fieldName));
            if (isSubTable && loadByXML)
            {
                if (table.Query.Context.Connection.Type == DatabaseType.PostgreSQL)
                {
                    sb.Append("XMLELEMENT(name ");
                    sb.Append(alias);
                    sb.Append(", ");
                }
                else if (table.Query.Context.Connection.Type == DatabaseType.Oracle)
                {
                    sb.Append("XMLELEMENT(");
                    sb.Append(alias);
                    sb.Append(", ");
                }
            }
            sb.Append(table.Alias);
            sb.Append(".");

            sb.Append(this.FormatDataElement(this.GetMappedFieldName(fieldName)));
            if (isSubTable && table.Query.Context.Connection.Type == DatabaseType.SQLServer)
            {
                sb.Append(" AS ");
                sb.Append(alias);
            }
            if (isSubTable && loadByXML && (table.Query.Context.Connection.Type == DatabaseType.PostgreSQL || table.Query.Context.Connection.Type == DatabaseType.Oracle))
            {
                sb.Append(")");
            }
            else if (isSubTable && !loadByXML && (table.Query.Context.Connection.Type == DatabaseType.MySQL || table.Query.Context.Connection.Type == DatabaseType.PostgreSQL || table.Query.Context.Connection.Type == DatabaseType.Oracle))
            {
                sb.Append(" AS ");
                sb.Append(alias);
            }

            return sb.ToString();
        }

        protected override void Dispose(bool disposing)
        {
            this.Logger.Dispose();
            this.Logger = null;
            base.Dispose(disposing);
            GC.SuppressFinalize(this);
        }
    }
}
