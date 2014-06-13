﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NBitcoin.Scanning
{
	public class ScanState : IDisposable
	{
		public ScanState(Scanner scanner,
						 Chain chain,
						 Account account, int startHeight)
		{
			if(scanner == null)
				throw new ArgumentNullException("scanner");


			_Account = account;
			_Chain = chain;
			_Scanner = scanner;
			_StartHeight = startHeight;
		}

		private readonly int _StartHeight;
		public int StartHeight
		{
			get
			{
				return _StartHeight;
			}
		}

		private readonly Scanner _Scanner;
		public Scanner Scanner
		{
			get
			{
				return _Scanner;
			}
		}


		private readonly Account _Account;
		public Account Account
		{
			get
			{
				return _Account;
			}
		}

		private readonly Chain _Chain;
		public Chain Chain
		{
			get
			{
				return _Chain;
			}
		}

		public bool CheckDoubleSpend
		{
			get;
			set;
		}

		public bool Process(Chain mainChain, IBlockProvider blockProvider)
		{
			bool newChain = false;
			if(!Chain.Initialized)
			{
				newChain = true;

				var firstBlock = mainChain.GetBlock(StartHeight);
				Chain.Initialize(firstBlock.Header, StartHeight);
			}
			var forkBlock = mainChain.FindFork(Chain);
			if(forkBlock.HashBlock != Chain.Tip.HashBlock)
			{
				Chain.SetTip(Chain.GetBlock(forkBlock.Height));
				foreach(var e in Account.GetInChain(Chain, false)
											.Where(e => e.Reason != AccountEntryReason.ChainBlockChanged))
				{
					var neutralized = e.Neutralize();
					Account.PushAccountEntry(neutralized);
				}
			}

			var unprocessedBlocks = mainChain.ToEnumerable(true)
									   .TakeWhile(block => block != forkBlock)
									   .Concat(newChain ? new ChainedBlock[] { forkBlock } : new ChainedBlock[0])
									   .Reverse().ToArray();
			foreach(var block in unprocessedBlocks)
			{
				List<byte[]> searchedData = new List<byte[]>();
				Scanner.GetScannedPushData(searchedData);
				foreach(var unspent in Account.Unspent)
				{
					searchedData.Add(unspent.OutPoint.ToBytes());
				}

				var fullBlock = blockProvider.GetBlock(block.HashBlock, searchedData);
				if(fullBlock == null)
					continue;

				List<Tuple<OutPoint, AccountEntry>> spents = new List<Tuple<OutPoint, AccountEntry>>();
				foreach(var spent in FindSpent(fullBlock, Account.Unspent))
				{
					var entry = new AccountEntry(AccountEntryReason.Outcome,
												block.HashBlock,
												spent, -spent.TxOut.Value);
					spents.Add(Tuple.Create(entry.Spendable.OutPoint, entry));
				}

				if(CheckDoubleSpend)
				{
					var spentsDico = spents.ToDictionary(t => t.Item1, t => t.Item2);
					foreach(var spent in Scanner.FindSpent(fullBlock))
					{
						if(!spentsDico.ContainsKey(spent.PrevOut))
							return false;
					}
				}

				foreach(var spent in spents)
				{
					if(Account.PushAccountEntry(spent.Item2) == null)
						return false;
				}

				foreach(var coins in Scanner.ScanCoins(fullBlock, (int)block.Height))
				{
					int i = 0;
					foreach(var output in coins.Coins.Outputs)
					{
						if(!output.IsNull)
						{
							var entry = new AccountEntry(AccountEntryReason.Income, block.HashBlock,
												new Spendable(new OutPoint(coins.TxId, i), output), output.Value);
							if(Account.PushAccountEntry(entry) == null)
								return false;
						}
						i++;
					}
				}

				Chain.GetOrAdd(block.Header);
			}
			return true;
		}

		public IEnumerable<Spendable> FindSpent(Block block, IEnumerable<Spendable> among)
		{
			var amongDico = among.ToDictionary(o => o.OutPoint);
			foreach(var spent in block
									.Transactions
									.Where(t=>!t.IsCoinBase)
									.SelectMany(t => t.Inputs)
									.Where(input => amongDico.ContainsKey(input.PrevOut)))
			{
				var spendable = amongDico[spent.PrevOut];
				amongDico.Remove(spent.PrevOut);
				yield return spendable;
			}
		}

		#region IDisposable Members

		public void Dispose()
		{
			Account.Entries.Dispose();
			Chain.Changes.Dispose();
		}

		#endregion
	}
}
