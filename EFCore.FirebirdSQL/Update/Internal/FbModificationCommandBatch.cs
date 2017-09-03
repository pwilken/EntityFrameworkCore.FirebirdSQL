/*                 
 *            FirebirdSql.EntityFrameworkCore.Firebird
 *     
 *              https://www.firebirdsql.org/en/net-provider/ 
 *              
 *     Permission to use, copy, modify, and distribute this software and its
 *     documentation for any purpose, without fee, and without a written
 *     agreement is hereby granted, provided that the above copyright notice
 *     and this paragraph and the following two paragraphs appear in all copies. 
 * 
 *     The contents of this file are subject to the Initial
 *     Developer's Public License Version 1.0 (the "License");
 *     you may not use this file except in compliance with the
 *     License. You may obtain a copy of the License at
 *     http://www.firebirdsql.org/index.php?op=doc&id=idpl
 *
 *     Software distributed under the License is distributed on
 *     an "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, either
 *     express or implied.  See the License for the specific
 *     language governing rights and limitations under the License.
 *
 *      Credits: Rafael Almeida (ralms@ralms.net)
 *                              Sergipe-Brazil
 *
 *
 *                              
 *                  All Rights Reserved.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Internal;
using System.Threading;
using System.Threading.Tasks;
using FirebirdSql.Data.FirebirdClient;

namespace Microsoft.EntityFrameworkCore.Update.Internal
{

    public class FbModificationCommandBatch  : AffectedCountModificationCommandBatch
    {
        internal const int MaxParameterCount = 1000;
        internal const int MaxRowCount = 256;
        internal int CountParameter = 1;
        internal readonly int _maxBatchSize;
        internal readonly IRelationalCommandBuilderFactory _commandBuilderFactory = null;
        internal readonly IRelationalValueBufferFactoryFactory _valueBufferFactory = null;
        
        internal readonly List<ModificationCommand> _BlockInsertCommands = new List<ModificationCommand>();
        internal readonly List<ModificationCommand> _BlockUpdateCommands = new List<ModificationCommand>();
        internal readonly List<ModificationCommand> _BlockDeleteCommands = new List<ModificationCommand>();

        internal readonly StringBuilder _ExecuteParameters = null;
        private string _Seperator = null;
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public FbModificationCommandBatch(
            [NotNull] IRelationalCommandBuilderFactory commandBuilderFactory,
            [NotNull] ISqlGenerationHelper sqlGenerationHelper,
            // ReSharper disable once SuggestBaseTypeForParameter
            [NotNull] IFbUpdateSqlGenerator updateSqlGenerator,
            [NotNull] IRelationalValueBufferFactoryFactory valueBufferFactoryFactory,
            [CanBeNull] int? maxBatchSize)
            : base(commandBuilderFactory, sqlGenerationHelper, updateSqlGenerator, valueBufferFactoryFactory)
        {
            if (maxBatchSize.HasValue && maxBatchSize.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBatchSize), RelationalStrings.InvalidMaxBatchSize);

            _maxBatchSize = Math.Min(maxBatchSize ?? int.MaxValue, MaxRowCount);
            _commandBuilderFactory = commandBuilderFactory;
            _valueBufferFactory = valueBufferFactoryFactory;
            _ExecuteParameters = new StringBuilder();
            _Seperator = string.Empty;

        

        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected new virtual IFbUpdateSqlGenerator UpdateSqlGenerator => (IFbUpdateSqlGenerator)base.UpdateSqlGenerator;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override bool CanAddCommand(ModificationCommand modificationCommand)
        {
            if (ModificationCommands.Count >= _maxBatchSize)
                return false;


            var additionalParameterCount = CountParameters(modificationCommand);
            if (CountParameter + additionalParameterCount >= MaxParameterCount)
                return false;


            CountParameter += additionalParameterCount;
            return true;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override bool IsCommandTextValid()
            => true;


        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override int GetParameterCount()
            => CountParameter;

        private static int CountParameters(ModificationCommand modificationCommand)
        {
            var parameterCount = 0;
            foreach (var columnModification in modificationCommand.ColumnModifications)
            {
                if (columnModification.UseCurrentValueParameter)
                    parameterCount++;

                if (columnModification.UseOriginalValueParameter)
                    parameterCount++;

            }
            return parameterCount;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override void ResetCommandText()
        {
            base.ResetCommandText();
            _BlockInsertCommands.Clear();
            _BlockUpdateCommands.Clear();
            _BlockDeleteCommands.Clear();
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override string GetCommandText()
        {
            var SbCommands     = new StringBuilder();
            var SbExecuteBlock = new StringBuilder();
            _ExecuteParameters.Clear();

            //Commands Insert/Update/Delete
            SbCommands.AppendLine(base.GetCommandText());
            SbCommands.Append(GetBlockInsertCommandText(ModificationCommands.Count));
            SbCommands.Append(GetBlockUpdateCommandText(ModificationCommands.Count));
            SbCommands.Append(GetBlockDeleteCommandText(ModificationCommands.Count));

            //Execute Block
            var parameters = _ExecuteParameters.ToString();
            SbExecuteBlock.Append("EXECUTE BLOCK ");
            if (parameters.Length > 0)
            {
                SbExecuteBlock.Append("( ");
                SbExecuteBlock.Append(parameters);
                SbExecuteBlock.Append(") ");
            }
            SbExecuteBlock.AppendLine($"RETURNS (AffectedRows BIGINT) AS BEGIN");
            SbExecuteBlock.Append("AffectedRows=0;");
            SbExecuteBlock.Append(SbCommands.ToString());
            SbExecuteBlock.AppendLine("END;");
            return SbExecuteBlock.ToString().Trim(); 
        }
         
        private string GetBlockDeleteCommandText(int lastIndex)
        {
            if (_BlockDeleteCommands.Count == 0)
                return string.Empty;


            var stringBuilder = new StringBuilder();
            var resultSetMapping = UpdateSqlGenerator.AppendBlockDeleteOperation(stringBuilder, _BlockDeleteCommands, lastIndex - _BlockDeleteCommands.Count);
            for (var i = lastIndex - _BlockDeleteCommands.Count; i < lastIndex; i++)
                CommandResultSet[i] = resultSetMapping;

            if (resultSetMapping != ResultSetMapping.NoResultSet)
                CommandResultSet[lastIndex - 1] = ResultSetMapping.LastInResultSet;

            return stringBuilder.ToString();
        }

        private string GetBlockUpdateCommandText(int lastIndex)
        {
            if (_BlockUpdateCommands.Count == 0)
                return string.Empty;


            var stringBuilder = new StringBuilder();
            var headStringBuilder = new StringBuilder();
            var resultSetMapping = UpdateSqlGenerator.AppendBlockUpdateOperation(stringBuilder, headStringBuilder,_BlockUpdateCommands, lastIndex - _BlockUpdateCommands.Count);
            for (var i = lastIndex - _BlockUpdateCommands.Count; i < lastIndex; i++)
                CommandResultSet[i] = resultSetMapping;

            if (resultSetMapping != ResultSetMapping.NoResultSet)
                CommandResultSet[lastIndex - 1] = ResultSetMapping.LastInResultSet;

            var ExecuteParameters = headStringBuilder.ToString();
            if (ExecuteParameters.Length > 0)
            {
                _ExecuteParameters.Append(_Seperator);
                _ExecuteParameters.Append(ExecuteParameters);
                _Seperator = ",";
            }

            return stringBuilder.ToString();
        }

        private string GetBlockInsertCommandText(int lastIndex)
        {
            if (_BlockInsertCommands.Count == 0)
                return string.Empty;

            var stringBuilder = new StringBuilder();
            var headStringBuilder = new StringBuilder();
            var resultSetMapping = UpdateSqlGenerator.AppendBlockInsertOperation(stringBuilder, headStringBuilder, _BlockInsertCommands, lastIndex - _BlockInsertCommands.Count);
            for (var i = lastIndex - _BlockInsertCommands.Count; i < lastIndex; i++)
                CommandResultSet[i] = resultSetMapping;

            if (resultSetMapping != ResultSetMapping.NoResultSet)
                CommandResultSet[lastIndex - 1] = ResultSetMapping.LastInResultSet;

            var ExecuteParameters = headStringBuilder.ToString();
            if (ExecuteParameters.Length > 0)
            {
                _ExecuteParameters.Append(_Seperator);
                _ExecuteParameters.Append(ExecuteParameters);
                _Seperator = ",";
            }
            
            return stringBuilder.ToString();
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override void UpdateCachedCommandText(int commandPosition)
        {
            var newModificationCommand = ModificationCommands[commandPosition];

            if (newModificationCommand.EntityState == EntityState.Added)
            {
                if (_BlockInsertCommands.Count > 0 
                    && !CanBeInsertedInSameStatement(_BlockInsertCommands[0], newModificationCommand))
                { 
                    CachedCommandText.Append(GetBlockInsertCommandText(commandPosition));
                    _BlockInsertCommands.Clear(); 
                } 
                _BlockInsertCommands.Add(newModificationCommand);
                LastCachedCommandIndex = commandPosition;
                

            }
            else if (newModificationCommand.EntityState == EntityState.Modified)
            {
                if (_BlockUpdateCommands.Count > 0
                    && !CanBeUpdateInSameStatement(_BlockUpdateCommands[0], newModificationCommand))
                {
                    CachedCommandText.Append(GetBlockUpdateCommandText(commandPosition));
                    _BlockUpdateCommands.Clear();
                }
                _BlockUpdateCommands.Add(newModificationCommand);
                LastCachedCommandIndex = commandPosition;
            }
            else if (newModificationCommand.EntityState == EntityState.Deleted)
            {
                if (_BlockDeleteCommands.Count > 0
                    && !CanBeDeleteInSameStatement(_BlockDeleteCommands[0], newModificationCommand))
                {
                    CachedCommandText.Append(GetBlockDeleteCommandText(commandPosition));
                    _BlockDeleteCommands.Clear();
                }
                _BlockDeleteCommands.Add(newModificationCommand);
                LastCachedCommandIndex = commandPosition;
            }
            else
            {
                CachedCommandText.Append(GetBlockInsertCommandText(commandPosition));
                _BlockInsertCommands.Clear(); 
                base.UpdateCachedCommandText(commandPosition);
            }
        } 

        private static bool CanBeDeleteInSameStatement(ModificationCommand firstCommand, ModificationCommand secondCommand)
          => string.Equals(firstCommand.TableName, secondCommand.TableName, StringComparison.Ordinal)
             && string.Equals(firstCommand.Schema, secondCommand.Schema, StringComparison.Ordinal)
             && firstCommand.ColumnModifications.Where(o => o.IsWrite).Select(o => o.ColumnName).SequenceEqual(
                 secondCommand.ColumnModifications.Where(o => o.IsWrite).Select(o => o.ColumnName))
             && firstCommand.ColumnModifications.Where(o => o.IsRead).Select(o => o.ColumnName).SequenceEqual(
                 secondCommand.ColumnModifications.Where(o => o.IsRead).Select(o => o.ColumnName));

        private static bool CanBeUpdateInSameStatement(ModificationCommand firstCommand, ModificationCommand secondCommand)
    => string.Equals(firstCommand.TableName, secondCommand.TableName, StringComparison.Ordinal)
       && string.Equals(firstCommand.Schema, secondCommand.Schema, StringComparison.Ordinal)
       && firstCommand.ColumnModifications.Where(o => o.IsWrite).Select(o => o.ColumnName).SequenceEqual(
           secondCommand.ColumnModifications.Where(o => o.IsWrite).Select(o => o.ColumnName))
       && firstCommand.ColumnModifications.Where(o => o.IsRead).Select(o => o.ColumnName).SequenceEqual(
           secondCommand.ColumnModifications.Where(o => o.IsRead).Select(o => o.ColumnName));

        private static bool CanBeInsertedInSameStatement(ModificationCommand firstCommand, ModificationCommand secondCommand)
            => string.Equals(firstCommand.TableName, secondCommand.TableName, StringComparison.Ordinal)
               && string.Equals(firstCommand.Schema, secondCommand.Schema, StringComparison.Ordinal)
               && firstCommand.ColumnModifications.Where(o => o.IsWrite).Select(o => o.ColumnName).SequenceEqual(
                   secondCommand.ColumnModifications.Where(o => o.IsWrite).Select(o => o.ColumnName))
               && firstCommand.ColumnModifications.Where(o => o.IsRead).Select(o => o.ColumnName).SequenceEqual(
                   secondCommand.ColumnModifications.Where(o => o.IsRead).Select(o => o.ColumnName));





        ///// <summary>
        ///// Make the Datareader consummation
        ///// </summary>
        ///// <param name = "reader" ></ param >
        protected override void Consume(RelationalDataReader relationalReader)
        {
             
            //Cast FbDataReader
            var _dataReader = (FbDataReader)relationalReader.DbDataReader; 
            var commandIndex = 0;
            try
            {

                for (; ; )
                {
                    while (commandIndex < CommandResultSet.Count
                          && CommandResultSet[commandIndex] == ResultSetMapping.NoResultSet)
                    {
                        commandIndex++;
                    } 
                    int propragation = commandIndex;
                    while (propragation < ModificationCommands.Count &&
                        !ModificationCommands[propragation].RequiresResultPropagation)
                        propragation++;

                    while (commandIndex < propragation)
                    {
                        commandIndex++;
                        if (!_dataReader.Read())
                        {
                            throw new DbUpdateConcurrencyException(
                                RelationalStrings.UpdateConcurrencyException(1, 0),
                                ModificationCommands[commandIndex].Entries
                            );
                        }
                    }
                    //check if you've gone through all notifications
                    if (propragation == ModificationCommands.Count)
                        break;

                    var modifications = ModificationCommands[commandIndex++];
                    if (!relationalReader.Read())
                    {
                        throw new DbUpdateConcurrencyException(
                            RelationalStrings.UpdateConcurrencyException(1, 0),
                            modifications.Entries);
                    }
                    var _bufferFactory = CreateValueBufferFactory(modifications.ColumnModifications);
                    modifications.PropagateResults(_bufferFactory.Create(_dataReader));
                    _dataReader.NextResult();
                }
            }
            catch (DbUpdateException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new DbUpdateException(
                    RelationalStrings.UpdateStoreException,
                    ex,
                    ModificationCommands[commandIndex].Entries);
            }
        }

        /// <summary>
        /// Method Async for Propagation DataReader
        /// </summary>
        /// <param name = "reader" ></ param >
        /// < param name="cancellationToken"></param>
        /// <returns></returns>
        protected override Task ConsumeAsync(RelationalDataReader reader, CancellationToken cancellationToken = default(CancellationToken))
            => Task.Run(() => Consume(reader));
    }

}
