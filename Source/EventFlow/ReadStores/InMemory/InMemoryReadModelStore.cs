﻿// The MIT License (MIT)
//
// Copyright (c) 2015 Rasmus Mikkelsen
// https://github.com/rasmus/EventFlow
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of
// this software and associated documentation files (the "Software"), to deal in
// the Software without restriction, including without limitation the rights to
// use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
// the Software, and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
// FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
// IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
// CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventFlow.Aggregates;
using EventFlow.Core;
using EventFlow.Logs;

namespace EventFlow.ReadStores.InMemory
{
    public class InMemoryReadModelStore<TReadModel, TReadModelLocator> :
        MultiAggregateReadModelStore<TReadModel, TReadModelLocator>,
        IInMemoryReadModelStore<TReadModel>
        where TReadModel : IReadModel, new()
        where TReadModelLocator : IReadModelLocator
    {
        private readonly AsyncLock _asyncLock = new AsyncLock();
        private readonly Dictionary<string, TReadModel> _readModels = new Dictionary<string, TReadModel>();

        public InMemoryReadModelStore(
            ILog log,
            TReadModelLocator readModelLocator,
            IReadModelDomainEventApplier readModelDomainEventApplier)
            : base(log, readModelLocator, readModelDomainEventApplier)
        {
        }

        private async Task UpdateReadModelAsync(
            string id,
            bool forceNew,
            IReadOnlyCollection<IDomainEvent> domainEvents,
            IReadModelContext readModelContext,
            CancellationToken cancellationToken)
        {
            using (await _asyncLock.WaitAsync(cancellationToken))
            {
                TReadModel readModel;
                if (_readModels.ContainsKey(id) && !forceNew)
                {
                    readModel = _readModels[id];
                }
                else
                {
                    readModel = new TReadModel();
                    _readModels[id] = readModel;
                }

                await ReadModelDomainEventApplier.UpdateReadModelAsync(
                    readModel,
                    domainEvents,
                    readModelContext,
                    cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        public TReadModel Get(IIdentity id)
        {
            TReadModel readModel;
            return _readModels.TryGetValue(id.Value, out readModel)
                ? readModel
                : default(TReadModel);
        }

        public IEnumerable<TReadModel> GetAll()
        {
            return _readModels.Values;
        }

        public IEnumerable<TReadModel> Find(Predicate<TReadModel> predicate)
        {
            return _readModels.Values.Where(rm => predicate(rm));
        }

        public override Task PopulateReadModelAsync<TReadModelToPopulate>(
            string id,
            IReadOnlyCollection<IDomainEvent> domainEvents,
            IReadModelContext readModelContext,
            CancellationToken cancellationToken)
        {
            return UpdateReadModelAsync(id, true, domainEvents, readModelContext, cancellationToken);
        }

        public override Task<TReadModel> GetByIdAsync(string id, CancellationToken cancellationToken)
        {
            TReadModel readModel;
            return _readModels.TryGetValue(id, out readModel)
                ? Task.FromResult(readModel)
                : Task.FromResult(default(TReadModel));
        }

        public override Task PurgeAsync<TReadModelToPurge>(CancellationToken cancellationToken)
        {
            if (typeof (TReadModel) == typeof(TReadModelToPurge))
            {
                _readModels.Clear();
            }
            return Task.FromResult(0);
        }

        protected override Task UpdateReadModelsAsync(
            IReadOnlyCollection<ReadModelUpdate> readModelUpdates,
            IReadModelContext readModelContext,
            CancellationToken cancellationToken)
        {
            var updateTasks = readModelUpdates
                .Select(rmu => UpdateReadModelAsync(rmu.ReadModelId, false, rmu.DomainEvents, readModelContext, cancellationToken));
            return Task.WhenAll(updateTasks);
        }
    }
}
