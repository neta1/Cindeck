using Cindeck.Core;
using PropertyChanged;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace Cindeck.ViewModels
{
    [ImplementPropertyChanged]
    class UnitViewModel:IViewModel, INotifyPropertyChanged
    {
        private AppConfig m_config;
        private MainViewModel m_mvm;

        public event PropertyChangedEventHandler PropertyChanged;

        public UnitViewModel(AppConfig config, MainViewModel mvm)
        {
            m_config = config;
            m_mvm = mvm;

            SendToSlotCommand = new DelegateCommand<string>(SendToSlot, x => SelectedIdol != null);
            SaveCommand = new DelegateCommand(Save, () => !string.IsNullOrEmpty(UnitName));
            DeleteCommand = new DelegateCommand(Delete, () => Units.Contains(SelectedUnit));
            OptimizeCommand = new DelegateCommand(Optimize, () => m_config.OwnedIdols.Count > 0);
            MoveToSlotCommand = new DelegateCommand<string>(MoveToSlot, CanMoveToSlot);
            ResetSlotCommand = new DelegateCommand<string>(ResetSlot, CanResetSlot);
            HighlightCommand = new DelegateCommand<string>(Highlight, CanHighlight);
            CopyIidCommand = new DelegateCommand(CopyIid, () => SelectedIdol != null);
            SetGuestCenterCommand = new DelegateCommand(SetGuestCenter, () => SelectedIdol != null);
            CopyIidFromSlotCommand = new DelegateCommand<string>(CopyIidFromSlot);
            SetGuestCenterFromSlotCommand = new DelegateCommand<string>(SetGuestCenterFromSlot);

            GrooveBursts = new List<Tuple<AppealType?, string>>
            {
                Tuple.Create(new AppealType?(),"なし" ),
                Tuple.Create((AppealType?)AppealType.Vocal,"Vo 150%"),
                Tuple.Create((AppealType?)AppealType.Dance,"Da 150%"),
                Tuple.Create((AppealType?)AppealType.Visual,"Vi 150%")
            };
            SongTypes = new List<Tuple<IdolCategory, string>>
            {
                Tuple.Create(IdolCategory.All, "All 30%"),
                Tuple.Create(IdolCategory.Cute, "Cu 30%"),
                Tuple.Create(IdolCategory.Cool, "Co 30%"),
                Tuple.Create(IdolCategory.Passion, "Pa 30%")
            };
            SongType = IdolCategory.All;
            OptimizeResults = new ObservableCollection<Tuple<Unit, string>>();

            Idols = new ListCollectionView(m_config.OwnedIdols);
            Filter = new IdolFilter(config, Idols, false);
            Filter.SetConfig(config.UnitIdolFilterConfig);

            Units = m_config.Units;

            TemporalUnit = new Unit();
            SelectedUnit = Units.FirstOrDefault();

            foreach (var option in config.UnitIdolSortOptions)
            {
                Idols.SortDescriptions.Add(option.ToSortDescription());
            }
        }

        public List<Tuple<AppealType?, string>> GrooveBursts
        {
            get;
        }

        public AppealType? GrooveBurst
        {
            get;
            set;
        }

        public List<Tuple<IdolCategory, string>> SongTypes
        {
            get;
        }

        public IdolCategory SongType
        {
            get;
            set;
        }

        public bool ContainRecovery
        {
            get;
            set;
        }

        public ObservableCollection<Tuple<Unit, string>> OptimizeResults
        {
            get;
        }

        public Unit SelectedOptimizeResult
        {
            get;
            set;
        }

        public ObservableCollection<Unit> Units
        {
            get;
        }

        public Unit TemporalUnit
        {
            get;
            set;
        }

        public Unit SelectedUnit
        {
            get;
            set;
        }

        public ICollectionView Idols
        {
            get;
        }

        public IdolFilter Filter
        {
            get;
        }

        public OwnedIdol SelectedIdol
        {
            get;
            set;
        }

        public string UnitName
        {
            get;
            set;
        }

        public DelegateCommand<string> SendToSlotCommand
        {
            get;
        }

        private void SendToSlot(string slot)
        {
            if(TemporalUnit.AlreadyInUnit(SelectedIdol))
            {
                MessageBox.Show("選択したアイドルはすでにこのユニットに編成されています");
            }
            else
            {
                TemporalUnit.GetType().GetProperty(slot).SetValue(TemporalUnit, SelectedIdol);
            }
        }

        public DelegateCommand SaveCommand
        {
            get;
        }

        private void Save()
        {
            var target = Units.FirstOrDefault(x => x.Name == UnitName);
            if (target==null)
            {
                var newUnit= TemporalUnit.Clone();
                newUnit.Name = UnitName;
                Units.Insert(0, newUnit);
                SelectedUnit = newUnit;
            }
            else
            {
                target.CopyFrom(TemporalUnit);
            }
            m_config.Save();
        }

        public DelegateCommand DeleteCommand
        {
            get;
        }

        private void Delete()
        {
            Units.Remove(SelectedUnit);
            SelectedUnit = Units.FirstOrDefault();
            if (SelectedUnit == null)
            {
                TemporalUnit = new Unit();
            }
            m_config.Save();
        }

        public DelegateCommand OptimizeCommand
        {
            get;
        }

        private void Optimize()
        {
            OptimizeResults.Clear();

            IEnumerable<IEnumerable<OwnedIdol>> result;
            var ownedIdols = m_config.OwnedIdols.GroupBy(x => x.Iid).Select(x => x.OrderByDescending(y => y.SkillLevel).First());
            var appealUpIdols = ownedIdols.Where(x => x.CenterEffect is CenterEffect.AppealUp || (x.CenterEffect is CenterEffect.ConditionalAppealUp && ((CenterEffect.ConditionalAppealUp)x.CenterEffect).Condition == AppealUpCondition.UnitContainsAllTypes));

            if (appealUpIdols.Any())
            {
                result = appealUpIdols
                    .GroupBy(x => x.CenterEffect.Name)
                    .Select(x => x.OrderByDescending(y => CalculateTotalAppeal(y, y.CenterEffect)).First())
                    .Select(x => TakeBest5(x, ownedIdols))
                    .OrderByDescending(x => x.Sum(y => CalculateTotalAppeal(y, x.First().CenterEffect)));
            }
            else
            {
                result = Enumerable.Repeat(TakeBest5(null, ownedIdols), 1);
            }

            foreach (var i in result.Take(5))
            {
                var c = i.Count();
                var u = new Unit();
                if (c > 0) u.Slot3 = i.ElementAt(0);
                if (c > 1) u.Slot2 = i.ElementAt(1);
                if (c > 2) u.Slot4 = i.ElementAt(2);
                if (c > 3) u.Slot1 = i.ElementAt(3);
                if (c > 4) u.Slot5 = i.ElementAt(4);
                OptimizeResults.Add(Tuple.Create(u, string.Format("#{0}: {1}", OptimizeResults.Count + 1, i.Sum(x => CalculateTotalAppeal(x, i.First().CenterEffect)))));
            }
            SelectedOptimizeResult = OptimizeResults.Select(x => x.Item1).FirstOrDefault();
        }

        private IEnumerable<OwnedIdol> TakeBest5(OwnedIdol center, IEnumerable<OwnedIdol> ownedIdols)
        {
            if (center != null)
            {
                if (center.CenterEffect is CenterEffect.AppealUp)
                {
                    var r = ownedIdols.OrderByDescending(x => x == center).ThenByDescending(x => CalculateTotalAppeal(x, center.CenterEffect)).Take(5);
                    if (ContainRecovery && !r.Any(x => x.Skill is Skill.Revival))
                    {
                        return r.Take(4).Concat(ownedIdols.Where(x => x.Skill is Skill.Revival)
                            .OrderByDescending(x => x.SkillLevel)
                            .ThenByDescending(x => CalculateTotalAppeal(x, center.CenterEffect))
                            .Take(1));
                    }
                    return r;
                }
                else if (center.CenterEffect is CenterEffect.ConditionalAppealUp && ((CenterEffect.ConditionalAppealUp)center.CenterEffect).Condition == AppealUpCondition.UnitContainsAllTypes)
                {
                    var orderedIdols = ownedIdols.OrderByDescending(x => CalculateTotalAppeal(x, center.CenterEffect));
                    var u = new List<OwnedIdol>(5);
                    u.Add(center);
                    if (!u.Any(x => x.Category == IdolCategory.Cute))
                    {
                        u.Add(orderedIdols.First(x => x.Category == IdolCategory.Cute));
                    }
                    if (!u.Any(x => x.Category == IdolCategory.Cool))
                    {
                        u.Add(orderedIdols.First(x => x.Category == IdolCategory.Cool));
                    }
                    if (!u.Any(x => x.Category == IdolCategory.Passion))
                    {
                        u.Add(orderedIdols.First(x => x.Category == IdolCategory.Passion));
                    }
                    u.AddRange(orderedIdols.Except(u).Take(5 - u.Count));
                    u.RemoveAt(0);
                    u.Sort((a, b) => CalculateTotalAppeal(a, center.CenterEffect).CompareTo(CalculateTotalAppeal(b, center.CenterEffect)) * -1);
                    u.Insert(0, center);
                    if (ContainRecovery && !u.Any(x => x.Skill is Skill.Revival))
                    {
                        for (int i = u.Count - 1; i >= 0; i--)
                        {
                            var rIdols = ownedIdols.Where(x => x.Skill is Skill.Revival)
                                .OrderByDescending(x => x.SkillLevel)
                                .ThenByDescending(x => CalculateTotalAppeal(x, center.CenterEffect));
                            OwnedIdol ri = null;
                            if (u.Count(x => x.Category == u[i].Category) == 1)
                            {
                                ri = rIdols.FirstOrDefault(x => x.Category == u[i].Category);
                            }
                            else
                            {
                                ri = rIdols.FirstOrDefault();
                            }
                            if (ri != null)
                            {
                                u[i] = ri;
                                break;
                            }
                        }
                    }
                    return u;
                }
                else
                {
                    throw new NotImplementedException();
                }
            }
            else
            {
                var r = ownedIdols.OrderByDescending(x => CalculateTotalAppeal(x, null)).Take(5);
                if (ContainRecovery && !r.Any(x => x.Skill is Skill.Revival))
                {
                    return r.Take(4).Concat(ownedIdols.Where(x => x.Skill is Skill.Revival)
                        .OrderByDescending(x => x.SkillLevel)
                        .ThenByDescending(x => CalculateTotalAppeal(x, null))
                        .Take(1));
                }
                return r;
            }
        }

        private int CalculateTotalAppeal(OwnedIdol idol, ICenterEffect effect)
        {
            return CalculateAppeal(idol, AppealType.Vocal, effect, SongType, GrooveBurst)
                 + CalculateAppeal(idol, AppealType.Dance, effect, SongType, GrooveBurst)
                 + CalculateAppeal(idol, AppealType.Visual, effect, SongType, GrooveBurst);
        }

        private int CalculateAppeal(OwnedIdol idol, AppealType type, ICenterEffect effect, IdolCategory songType, AppealType? grooveType)
        {
            if (idol == null)
            {
                return 0;
            }

            var rawValue = (int)idol.GetType().GetProperty(type.ToString()).GetValue(idol);
            double upRate = 0;

            if (effect != null)
            {
                if (effect is CenterEffect.AppealUp)
                {
                    var e = effect as CenterEffect.AppealUp;
                    if (e.Targets.HasFlag(idol.Category) == true && e.TargetAppeal.HasFlag(type) == true)
                    {
                        upRate += e.Rate;
                    }
                }
                else if (effect is CenterEffect.ConditionalAppealUp)
                {
                    var e = effect as CenterEffect.ConditionalAppealUp;
                    if (e.Targets.HasFlag(idol.Category) == true && e.TargetAppeal.HasFlag(type) == true)
                    {
                        upRate += e.Rate;
                    }
                }
            }

            if (songType.HasFlag(idol.Category)) upRate += 0.3;
            if (grooveType.HasValue && grooveType.Value == type) upRate += 1.5;

            return (int)Math.Ceiling(rawValue + rawValue * upRate);
        }

        public DelegateCommand<string> MoveToSlotCommand
        {
            get;
        }

        private void MoveToSlot(string target)
        {
            var s = target.Split(',');
            var source = s[0];
            var dest = s[1];

            var srcIdol=TemporalUnit.GetType().GetProperty(source).GetValue(TemporalUnit);
            var dstIdol= TemporalUnit.GetType().GetProperty(dest).GetValue(TemporalUnit);

            TemporalUnit.GetType().GetProperty(source).SetValue(TemporalUnit, dstIdol);
            TemporalUnit.GetType().GetProperty(dest).SetValue(TemporalUnit, srcIdol);
        }

        private bool CanMoveToSlot(string target)
        {
            return TemporalUnit.GetType().GetProperty(target.Split(',')[0]).GetValue(TemporalUnit) != null;
        }

        public DelegateCommand<string> ResetSlotCommand
        {
            get;
        }

        private void ResetSlot(string target)
        {
            TemporalUnit.GetType().GetProperty(target).SetValue(TemporalUnit,null);
        }

        private bool CanResetSlot(string target)
        {
            return TemporalUnit.GetType().GetProperty(target).GetValue(TemporalUnit) != null;
        }

        public DelegateCommand<string> HighlightCommand
        {
            get;
        }

        private void Highlight(string target)
        {
            SelectedIdol = TemporalUnit.GetType().GetProperty(target).GetValue(TemporalUnit) as OwnedIdol;
        }

        private bool CanHighlight(string target)
        {
            return TemporalUnit.GetType().GetProperty(target).GetValue(TemporalUnit) != null;
        }

        public DelegateCommand CopyIidCommand
        {
            get;
        }

        private void CopyIid()
        {
            try
            {
                Clipboard.SetText(SelectedIdol.Iid.ToString("x8"));
            }
            catch
            {

            }
        }

        public DelegateCommand SetGuestCenterCommand
        {
            get;
            private set;
        }

        private void SetGuestCenter()
        {
            m_mvm.Simulation.GuestIid = SelectedIdol.Iid;
        }

        public DelegateCommand<string> CopyIidFromSlotCommand
        {
            get;
        }

        private void CopyIidFromSlot(string slot)
        {
            try
            {
                Clipboard.SetText(((IIdol)TemporalUnit.GetType().GetProperty(slot).GetValue(TemporalUnit)).Iid.ToString("x8"));
            }
            catch
            {

            }
        }

        public DelegateCommand<string> SetGuestCenterFromSlotCommand
        {
            get;
            private set;
        }

        private void SetGuestCenterFromSlot(string slot)
        {
            m_mvm.Simulation.GuestIid = ((IIdol)TemporalUnit.GetType().GetProperty(slot).GetValue(TemporalUnit)).Iid;
        }

        public void OnPropertyChanged(string propertyName, object before, object after)
        {
            if (propertyName == nameof(SelectedIdol))
            {
                SendToSlotCommand.RaiseCanExecuteChanged();
                CopyIidCommand.RaiseCanExecuteChanged();
            }
            else if (propertyName == nameof(UnitName))
            {
                SaveCommand.RaiseCanExecuteChanged();
            }
            else if (propertyName == nameof(SelectedUnit))
            {
                if (SelectedUnit != null)
                {
                    TemporalUnit.CopyFrom(SelectedUnit);
                }
                DeleteCommand.RaiseCanExecuteChanged();
            }
            else if (propertyName == nameof(SelectedOptimizeResult))
            {
                if (SelectedOptimizeResult != null)
                {
                    TemporalUnit.Slot1 = SelectedOptimizeResult.Slot1;
                    TemporalUnit.Slot2 = SelectedOptimizeResult.Slot2;
                    TemporalUnit.Slot3 = SelectedOptimizeResult.Slot3;
                    TemporalUnit.Slot4 = SelectedOptimizeResult.Slot4;
                    TemporalUnit.Slot5 = SelectedOptimizeResult.Slot5;
                }
                DeleteCommand.RaiseCanExecuteChanged();
            }

            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public bool IsIdolInUse(OwnedIdol idol)
        {
            return TemporalUnit.OccupiedByUnit(idol) || m_config.Units.Any(x => x.OccupiedByUnit(idol));
        }

        public void RemoveIdolFromUnits(OwnedIdol idol)
        {
            TemporalUnit.RemoveIdol(idol);
            foreach(var x in Units)
            {
                x.RemoveIdol(idol);
            }
        }

        public void Dispose()
        {
            m_config.UnitIdolSortOptions.Clear();
            foreach (var x in Idols.SortDescriptions)
            {
                m_config.UnitIdolSortOptions.Add(x.ToSortOption());
            }
            m_config.UnitIdolFilterConfig = Filter.GetConfig();
        }

        public void OnActivate()
        {
            Idols.Refresh();
            TemporalUnit.Timestamp = DateTime.Now;
        }

        public void OnDeactivate()
        {
            
        }
    }
}
