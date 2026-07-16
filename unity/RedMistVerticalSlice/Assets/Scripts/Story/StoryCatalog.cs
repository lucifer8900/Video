using System.Collections.Generic;

namespace Lingmai.RedMist
{
    public static class LegacyStoryCatalog
    {
        private static Dictionary<string, StoryNode> _nodes;

        public static IReadOnlyDictionary<string, StoryNode> Nodes => _nodes ??= Build();

        public static StoryNode Get(string id)
        {
            return Nodes.TryGetValue(id, out StoryNode node) ? node : null;
        }

        private static StoryNode Node(
            string id,
            string title,
            string location,
            string speaker,
            string male,
            string female,
            string background,
            string portrait,
            NodeKind kind,
            int minutes,
            params ChoiceDefinition[] choices)
        {
            StoryNode node = new StoryNode
            {
                id = id,
                title = title,
                location = location,
                speaker = speaker,
                maleText = male,
                femaleText = female,
                background = background,
                portrait = portrait,
                kind = kind,
                estimatedMinutes = minutes
            };
            node.choices.AddRange(choices);
            return node;
        }

        private static Dictionary<string, StoryNode> Build()
        {
            var nodes = new Dictionary<string, StoryNode>();

            nodes["prologue"] = Node(
                "prologue",
                "雾门将启",
                "赤雾秘苑外环",
                "旁白",
                "第三日的钟声还没有响，山腹中的赤雾已经像潮水一样起伏。七派弟子都在等那道只开启片刻的裂隙。沈砚站在人群最后，先看退路，再看同伴，最后才看远处被雾吞没的药谷。进入秘苑的人都说自己来求筑基灵药，可真正带进山里的，还有债、秘密和杀心。",
                "第三日的钟声还没有响，山腹中的赤雾已经像潮水一样起伏。蔽月宫弟子列成月轮阵，所有人都在等楚明绮下令。她的真实境界与眼前可调用的法力并不相同；若过早暴露赤鸾焰环，门人可以少死几个，但各宗长老也会立刻重新估量她。",
                "red_mist_weather",
                "",
                NodeKind.Narrative,
                2,
                new ChoiceDefinition("进入临时营地，检查行装", "to_camp", "保留完整补给选择", "camp"),
                new ChoiceDefinition("沿水面观察雾流，绕到侧门", "gate_scout", "消耗神识，跳过营地并提前识别伏击痕迹", "alliance"),
                new ChoiceDefinition("与同伴合流后共同入苑", "gate_companion", "信任提升，再进入营地准备", "camp"));

            nodes["camp"] = Node(
                "camp",
                "营地与底牌",
                "外环临时营地",
                "内心",
                "火堆旁摆着两张匿息符、一瓶回气散和一枚尚未完全摸清禁制的金蜉连环刃。全部带走最稳妥，却会让纳物囊臃肿；少带一样，飞遁和撤退便能多留几分余地。顾惊雨压低声音提醒：入口附近已有陌生神识扫过两次。",
                "三名年轻弟子的法力都未恢复，伤员还在发热。楚明绮可以把宗门护符分给队伍，也可以把灵力留给维持身份伪装。长老的传讯只有一句：主药必须带回。她却清楚，活着回去的弟子同样会成为宗门未来。",
                "red_mist_modules",
                "",
                NodeKind.Narrative,
                2,
                new ChoiceDefinition("保留撤退法力，轻装入苑", "camp_retreat", "法力+10，护符较少", "alliance"),
                new ChoiceDefinition("带齐符箓与回气散", "camp_balanced", "获得额外护符，飞行负担增加", "alliance"),
                new ChoiceDefinition("把护符交给同伴", "camp_protect", "信任提升，自身防护下降", "alliance"));

            nodes["alliance"] = Node(
                "alliance",
                "临时同行",
                "雾门石阶",
                "顾惊雨／蔽月宫弟子",
                "顾惊雨愿意同行，却会因此看见更多底牌。另一名同门提出交换情报：他知道一条避开正门伏击的旧兽径，但要求事后分得一株主药。沈砚无法验证情报真假，只能从对方靴底的新泥和袖口的断丝判断，他确实刚从那条路退回来。",
                "两名外宗弟子请求并入队伍。他们声称愿意听从指挥，目光却一直停在蔽月宫的阵旗上。年轻门人等待楚明绮表态：接纳他们能扩大侦察面，也可能把队伍的虚实交给别宗。拒绝最安全，却会在秘苑里多出一群心怀不满的旁观者。",
                "red_mist_establishing",
                "",
                NodeKind.Narrative,
                1,
                new ChoiceDefinition("接受同行，但只共享路线", "ally_cautious", "信任小幅提升，秘密仍受保护", "flight"),
                new ChoiceDefinition("交换完整情报并共同进退", "ally_open", "信任提升，暴露风险增加", "flight"),
                new ChoiceDefinition("拒绝组队，保持距离", "ally_refuse", "秘密安全，但后续救援更困难", "flight"));

            nodes["flight"] = Node(
                "flight",
                "御器越隘",
                "狭天隘上空",
                "系统",
                "山隘两侧险峰夹成窄缝，地面丝线与焦痕说明伏击者已封住步行路线。短程御器可以抢在雾门闭合前抵达中心区，但飞得越高越容易成为靶子。保持低空、借雾遮蔽，并收集沿途散逸灵气，才能为撤退保留法力。",
                "石壁间的风正在改变。队伍若沿谷底前进，很可能错过入口；若结阵飞越，伤员会拖慢所有人。楚明绮必须在低空领航，避开乱流与侦测光，同时用最少法力为弟子打开安全航线。",
                "flight_sky-v2",
                "",
                NodeKind.Flight,
                2);

            nodes["herb_route"] = Node(
                "herb_route",
                "雾中药径",
                "环岳药谷",
                "旁白",
                "雾散的一刻，四种灵植同时显露。真正的玉髓芝应当根须藏白、叶背有冷露，附近却没有守药妖兽留下的足迹。有人故意移栽了赝品，也有人在真药周围布下引兽粉。选错并非只损失时间，还会把藏在雾后的东西引来。",
                "药谷里四处都是被翻动过的泥土。弟子认出了紫猴花，却没有注意花茎上的热斑。楚明绮必须让队伍在争药欲望压过纪律前停下来：先辨认、再采集、最后清理气味，任何一步省略都可能让整队暴露。",
                "red_mist_establishing",
                "",
                NodeKind.HerbGathering,
                2);

            nodes["corpse_signs"] = Node(
                "corpse_signs",
                "无声的伏击",
                "密林溪涧",
                "内心",
                "溪水旁躺着一名外宗弟子，纳物囊还在，致命伤却不是妖兽造成。树皮上有三道极细的割痕，泥中还残留被火烤干的蛛丝。放出神识可以复原袭击方向，也可能惊动仍在附近的人。",
                "失联弟子的佩剑插在溪边，剑穗朝向与尸体倒下的方向相反。有人移动过现场。楚明绮若亲自探查，伪装法力会出现短暂波动；若让年轻弟子搜索，他们可能踩中仍未解除的第二层陷阱。",
                "red_mist_modules",
                "",
                NodeKind.DivineSense,
                2);

            nodes["rescue"] = Node(
                "rescue",
                "雾后的呼救",
                "石墙外缘",
                "受伤弟子",
                "雾后传来两短一长的敲石声。那是七派共用的求援暗号，也可能是伏击者从死者身上学来的诱饵。入口还有一刻钟关闭；绕行最稳，救人会失去争药先机，而顺势设伏则可能把真正的伤员也当成棋子。",
                "敲石声来自蔽月宫旧暗号，回应却慢了半拍。楚明绮知道失联门人就在附近，也知道敌人可能正借她的责任设局。宗门领队最难的选择从来不是救不救，而是愿意让多少人共同承担这次救援的代价。",
                "red_mist_weather",
                "",
                NodeKind.Narrative,
                2,
                new ChoiceDefinition("布置掩护后救援", "rescue_careful", "消耗时间和护符，较安全", "shijun"),
                new ChoiceDefinition("立即冲入雾中救人", "rescue_rush", "节省时间，但容易负伤", "shijun"),
                new ChoiceDefinition("记录位置，先去开启入口", "rescue_skip", "保留资源，关系与宗门评价下降", "shijun"));

            nodes["shijun"] = Node(
                "shijun",
                "石峻的价码",
                "四铜门前",
                "石峻",
                "石峻从铜门阴影中现身，手里转着一枚染血阵钉。他没有立刻攻击，只提出交换：交出刚采到的灵药，他便说出地下入口的位置。沈砚注意到阵钉上的泥仍在冒热气——对方刚从地下败退，所谓情报很可能只是一次借刀杀人。",
                "石峻拦住队伍，声称失联弟子已经进入地下，还展示一枚蔽月宫腰牌。腰牌是真的，话却未必。他在等待楚明绮因焦急而暴露修为；只要她先动手，其他宗门便会把冲突记成蔽月宫强夺入口。",
                "red_mist_modules",
                "shi_jun-v1",
                NodeKind.Narrative,
                2,
                new ChoiceDefinition("示弱并追问地下细节", "shijun_probe", "可能识破谎言，保留底牌", "formation"),
                new ChoiceDefinition("以境界威慑，逼他让路", "shijun_threat", "快速通过，但增加暴露", "formation"),
                new ChoiceDefinition("交出一株次药换取阵钉", "shijun_trade", "损失资源，获得破阵优势", "formation"));

            nodes["formation"] = Node(
                "formation",
                "四门禁制",
                "中心区古铜门",
                "系统",
                "四道铜门并不对应四条路。月阳宝珠的光依次落在旧铜、青石、药叶和水面上，形成一条不断重复的灵气回路。按错误顺序触碰阵眼会唤醒守药兽；强攻则会消耗本应用于墨蛟战的法力。",
                "阵法残缺，却仍在按照旧日巡行规律转动。楚明绮看出四个阵眼中只有三个需要激活，最后一个必须保持沉寂，才能为队伍留下撤退缺口。年轻弟子已经举起阵旗，等待她给出顺序。",
                "red_mist_weather",
                "",
                NodeKind.Formation,
                2);

            nodes["underground"] = Node(
                "underground",
                "玄泥蛟窟",
                "苍岩殿地下沼泽",
                "旁白",
                "青石殿下方并不是地宫，而是一片方圆数里的黑泥沼泽。热气从泥泡中喷出，白玉小亭悬在中央，暗金箱匣浮于亭上。楚明绮站在另一侧玉栏后，身边还有一名负伤弟子。两人尚未开口，泥面已经出现第二道涟漪。",
                "地下热风扯动面纱。对面那个法力不高的青衣修士没有抢先靠近金箱，反而先在黑土堆间标出三条撤退线。楚明绮认出这种谨慎，也看见墨蛟正从他的盲区潜近。双方若不能在第一击前建立最低限度的信任，任何宝物都只会成为诱饵。",
                "dragon_cavern-v2",
                "",
                NodeKind.Narrative,
                2,
                new ChoiceDefinition("提出共享情报、各保底牌", "meet_cautious", "建立谨慎同盟", "combat_one"),
                new ChoiceDefinition("先提醒对方墨蛟位置", "meet_warn", "信任提升，放弃偷袭优势", "combat_one"),
                new ChoiceDefinition("沉默占据退路，等待对方先动", "meet_wait", "保留主动权，增加猜疑", "combat_one"));

            nodes["combat_one"] = Node(
                "combat_one",
                "墨蛟战·试探",
                "玄泥蛟窟",
                "战斗",
                "墨蛟破泥而出，鳞片把月光石反射成碎白。它没有立刻扑向最近的人，而是以长尾封住狭道。沈砚必须决定第一轮目标：限制蛟尾、保护伤员、试探鳞甲弱点，或趁撤退窗口尚在时离开。",
                "墨蛟的第一击不是冲撞，而是把滚烫黑泥掀向蔽月宫弟子。楚明绮可以控制区域、组织撤离，也可以短暂释放赤鸾火压制妖蛟；越强的手段越能救人，也越难继续隐藏真实状态。",
                "dragon_attack-v1",
                "",
                NodeKind.CombatOne,
                2);

            nodes["combat_two"] = Node(
                "combat_two",
                "墨蛟战·底牌",
                "白玉亭与黑泥沼泽",
                "战斗",
                "墨蛟被逼离泥面，却开始撞击支撑白玉亭的阵柱。再拖一轮，金箱与灵药都会沉入黑泥。沈砚可以发动金蜉连环刃、引爆预置符箓、利用蒸汽遮挡合击，或保留最后法力带人撤退。",
                "阵柱正在断裂，负伤弟子已经无法自行离开。楚明绮可以公开赤鸾焰环完成压制，也可以借沈砚的符阵把火光伪装成环境异变。前者更稳，后者需要真正信任一个刚认识的人。",
                "dragon_attack-v1",
                "",
                NodeKind.CombatTwo,
                2);

            nodes["aftermath"] = Node(
                "aftermath",
                "战利品与秘密",
                "坍塌前的白玉亭",
                "旁白",
                "墨蛟退入黑泥，或终于不再动弹。金箱禁制仍未完全解除，出口却在收缩。此刻拿走什么、救下谁、是否追击，以及愿不愿意替临时盟友隐瞒底牌，比战斗本身更能决定两条修仙路日后的形状。",
                "泥面恢复平静只是暂时的。弟子、金箱、墨蛟材料和同盟者的秘密，不可能全部带走。楚明绮必须作出领队的最后决定，并接受回到宗门后有人会追问每一处缺失。",
                "dragon_cavern-v2",
                "",
                NodeKind.Narrative,
                2,
                new ChoiceDefinition("先救伤员，放弃核心金箱", "loot_rescue", "关系与宗门评价提升，资源较少", "ending"),
                new ChoiceDefinition("分取墨蛟材料并替盟友保密", "loot_share", "形成谨慎同盟，双方都有收获", "ending"),
                new ChoiceDefinition("取走主药后立即撤离", "loot_escape", "保存自身成长资源，留下人情债", "ending"));

            nodes["ending"] = Node(
                "ending",
                "秘苑余烬",
                "赤雾秘苑出口",
                "结局",
                "雾门在身后合拢。有人带回主药，有人带回伤者，也有人只带回一条以后绝不会再走错的退路。沈砚没有得到所有东西，却确认了一件更重要的事：真正的底牌不是某件法器，而是在别人都被宝物吸引时，仍能判断自己愿意付出什么。",
                "雾门在身后合拢。弟子开始清点伤亡，长老的传讯也已经抵达。楚明绮知道宗门会评价她带回多少资源，却无法替她定义这次选择。她保住或暴露的力量、救下或放弃的人，都会在下一次命令到来时站在她身边。",
                "red_mist_establishing",
                "",
                NodeKind.Ending,
                1);

            nodes["prologue"].introMediaRef = "fmv_gate_arrival";
            nodes["flight"].introMediaRef = "fmv_celestial_flight";
            nodes["herb_route"].introMediaRef = "fmv_herb_courtyard";
            nodes["underground"].introMediaRef = "fmv_sword_vault";

            nodes["flight"].transitions.AddRange(new[]
            {
                new StoryTransitionDefinition("flight_perfect", "herb_route"),
                new StoryTransitionDefinition("flight_success", "herb_route"),
                new StoryTransitionDefinition("flight_failure", "corpse_signs")
            });
            nodes["herb_route"].transitions.AddRange(new[]
            {
                new StoryTransitionDefinition("herb_perfect", "corpse_signs"),
                new StoryTransitionDefinition("herb_success", "corpse_signs"),
                new StoryTransitionDefinition("herb_failure", "rescue")
            });
            nodes["corpse_signs"].transitions.AddRange(new[]
            {
                new StoryTransitionDefinition("scan_perfect", "rescue"),
                new StoryTransitionDefinition("scan_success", "rescue"),
                new StoryTransitionDefinition("scan_failure", "rescue")
            });
            nodes["formation"].transitions.AddRange(new[]
            {
                new StoryTransitionDefinition("formation_perfect", "underground"),
                new StoryTransitionDefinition("formation_success", "underground"),
                new StoryTransitionDefinition("formation_failure", "combat_one"),
                new StoryTransitionDefinition("formation_spike_bypass", "underground")
            });
            nodes["combat_one"].transitions.AddRange(new[]
            {
                new StoryTransitionDefinition("probe_blades", "combat_two"),
                new StoryTransitionDefinition("ward_tail", "combat_two"),
                new StoryTransitionDefinition("protect_ally", "combat_two"),
                new StoryTransitionDefinition("retreat", "ending"),
                new StoryTransitionDefinition("moon_control", "combat_two"),
                new StoryTransitionDefinition("phoenix_flash", "combat_two")
            });
            nodes["combat_two"].transitions.AddRange(new[]
            {
                new StoryTransitionDefinition("trump_blades", "aftermath"),
                new StoryTransitionDefinition("formation_burst", "aftermath"),
                new StoryTransitionDefinition("joint_strike", "aftermath"),
                new StoryTransitionDefinition("retreat", "ending"),
                new StoryTransitionDefinition("phoenix_ring", "aftermath"),
                new StoryTransitionDefinition("hold_line", "aftermath")
            });

            return nodes;
        }
    }
}
