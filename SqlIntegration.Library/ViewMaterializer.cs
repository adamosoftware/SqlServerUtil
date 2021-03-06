﻿using Dapper;
using Dapper.CX.SqlServer;
using SqlIntegration.Library.Extensions;
using SqlIntegration.Library.Queries;
using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace SqlIntegration.Library
{
    public abstract class ViewMaterializer<TKeyColumns>
    {
        private const string schema = "vm";
        private const string tableName = "SyncVersion";

        private static async Task SetVersionAsync(SqlConnection connection, DbObject view, long version)
        {
            await new SqlServerCmd($"{schema}.{tableName}", "Id")
            {
                { "#ObjectName", view.ToString() },
                { "LatestVersion", version }
            }.MergeAsync<int>(connection);
        }

        private static async Task<long> GetSyncVersionAsync(SqlConnection connection, DbObject view)
        {
            return await connection.QuerySingleOrDefaultAsync<long>($"SELECT [LatestVersion] FROM [{schema}].[{tableName}] WHERE [ObjectName]=@viewName", new { viewName = view.ToString() });
        }
        
        protected abstract string SourceView { get; }
        protected abstract string IntoTable { get; }
        protected abstract Task<IEnumerable<TKeyColumns>> GetChangesAsync(SqlConnection connection, long version);

        /// <summary>
        /// Remove version history for this view.
        /// This is usually for unit testing only, do not use this without understanding implications.
        /// </summary>
        public async Task ClearVersionAsync(SqlConnection connection)
        {
            await connection.ExecuteAsync($"DELETE [{schema}].[{tableName}] WHERE [ObjectName]=@viewName", new { viewName = SourceView });
        }

        public async Task ExecuteAsync(SqlConnection connection)
        {
            var sourceObj = DbObject.Parse(SourceView);
            var intoObj = DbObject.Parse(IntoTable);
            await InitializeAsync(connection, sourceObj, intoObj);

            var version = await GetSyncVersionAsync(connection, sourceObj);
            
            var changes = 
                (version != 0) ? await GetChangesAsync(connection, version) :
                await GetAllSourceRows(connection);

            string criteria = GetWhereClause();
            string columnList = await GetColumnListAsync(connection, sourceObj);

            foreach (var change in changes)
            {
                await connection.ExecuteAsync(
                    $"DELETE {intoObj.Delimited()} WHERE {criteria}", change);

                await connection.ExecuteAsync(
                    $@"INSERT INTO {intoObj.Delimited()} ({columnList}) 
                    SELECT {columnList} 
                    FROM {sourceObj.Delimited()}
                    WHERE {criteria}", change);
            }           

            long currentVersion = await GetCurrentVersionAsync(connection);
            await SetVersionAsync(connection, sourceObj, currentVersion);
        }

        private async Task<IEnumerable<TKeyColumns>> GetAllSourceRows(SqlConnection connection)
        {
            return await connection.QueryAsync<TKeyColumns>($"SELECT * FROM {DbObject.Delimited(SourceView)}");
        }

        private async Task InitializeAsync(SqlConnection connection, DbObject sourceView, DbObject intoTable)
        {
            if (sourceView.Equals(intoTable)) throw new InvalidOperationException("Source view and table cannot be the same.");

            if (!await connection.ViewExistsAsync(sourceView)) throw new ArgumentException($"View not found: {sourceView}");

            if (!await connection.SchemaExistsAsync(intoTable.Schema))
            {
                await connection.ExecuteAsync($"CREATE SCHEMA [{intoTable.Schema}]");
            }

            if (!(await connection.TableExistsAsync(intoTable)))
            {
                var pkColumns = GetColumnNames();
                string createTable = await Util.GetViewAsTableDefinitionAsync(connection, sourceView, intoTable, pkColumns?.ToArray());
                await connection.ExecuteAsync(createTable);
            }
            
            if (!(await connection.SchemaExistsAsync(schema)))
            {
                await connection.ExecuteAsync($"CREATE SCHEMA [{schema}]");
            }
            
            if (!(await connection.TableExistsAsync(schema, tableName)))
            {
                await connection.ExecuteAsync(
                    $@"CREATE TABLE [{schema}].[{tableName}] (
                        [ObjectName] nvarchar(255) NOT NULL,
                        [LatestVersion] bigint NOT NULL,
                        [Id] int identity(1,1),
                        CONSTRAINT [PK_{schema}_{tableName}] PRIMARY KEY ([ObjectName]),
                        CONSTRAINT [U_{schema}_{tableName}] UNIQUE ([Id])
                    )");
            }
        }

        private static async Task<string> GetColumnListAsync(SqlConnection connection, DbObject view)
        {            
            var columns = await new ViewColumns() { SchemaName = view.Schema, ViewName = view.Name }.ExecuteAsync(connection);
            return string.Join(", ", columns.Select(col => $"[{col.Name}]"));
        }

        private static IEnumerable<string> GetColumnNames()
        {
            var props = typeof(TKeyColumns).GetProperties();
            return props.Select(pi => pi.Name);
        }

        private static string GetWhereClause()
        {
            var props = typeof(TKeyColumns).GetProperties();
            return string.Join(" AND ", props.Select(pi => $"[{pi.Name}]=@{pi.Name}"));
        }

        private async Task<long> GetCurrentVersionAsync(SqlConnection connection)
        {
            try
            {
                return await connection.QuerySingleAsync<long>("SELECT CHANGE_TRACKING_CURRENT_VERSION()");
            }
            catch 
            {
                return 0;
            }
        }

        /// <summary>
        /// This is for unit testing only to be able to prove that the source view and output (i.e reporting) table are the same
        /// </summary>
        public async Task<bool> SourceViewEqualsResultTable(SqlConnection cn)
        {
            var props = typeof(TKeyColumns).GetProperties();
            string orderBy = string.Join(", ", props.Select(pi => $"[{pi.Name}]"));

            var viewSource = await cn.QueryAsync<TKeyColumns>($"SELECT * FROM {DbObject.Delimited(SourceView)} ORDER BY {orderBy}");
            var tableData = await cn.QueryAsync<TKeyColumns>($"SELECT * FROM {DbObject.Delimited(IntoTable)} ORDER BY {orderBy}");
            return viewSource.SequenceEqual(tableData);
        }

    }
}
