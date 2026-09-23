using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Dish
{
    public class Recipe
    {
        public int Id;
        public string Title;
        public string Category;
        public string Description;
        public string[] Ingredients;
        public string[] Steps;
        public string IngredientText;
        public string DirectionsText;
        public int PrepMin;
        public int CookMin;
        public int TotalMin;
        public string Base;          // primary dish family
        public List<string> Bases = new List<string>();
        public string Temp;          // hot | cold
        public string Season;        // summer | winter | any
        public int SummerScore;
        public int WinterScore;
        public int Bone;             // 0..100 osteoporosis friendliness
        public List<string> BoneWhy = new List<string>();
        public string Protein;
        public List<string> Diet = new List<string>();
        public string Cuisine;
        public string Effort;        // easy | medium
        public int Diff;             // 1 (very easy) .. 5 (most work)
        public string DiffLabel;
        public int Top;              // 0..5 editorial / community standing
        public bool OnePot;
        public bool Fried;
        public int Quality;
        public int Servings;         // as stated by the source; 0 = not stated
    }

    public static class Builder
    {
        // ---------------------------------------------------------------- utils
        static Regex Word(string phrase)
        {
            string p = Regex.Escape(phrase);
            p = p.Replace("\\ ", " ").Replace(" ", "[\\s-]+");
            return new Regex("(?<![a-z])" + p + "(?:es|s|ed)?(?![a-z])", RegexOptions.Compiled);
        }

        static Dictionary<string, Regex> _cache = new Dictionary<string, Regex>();
        static bool Has(string text, string phrase)
        {
            Regex r;
            if (!_cache.TryGetValue(phrase, out r)) { r = Word(phrase); _cache[phrase] = r; }
            return r.IsMatch(text);
        }
        static bool HasAny(string text, string[] phrases)
        {
            for (int i = 0; i < phrases.Length; i++) if (Has(text, phrases[i])) return true;
            return false;
        }
        static int CountAny(string text, string[] phrases)
        {
            int n = 0;
            for (int i = 0; i < phrases.Length; i++) if (Has(text, phrases[i])) n++;
            return n;
        }

        // Parses the JSON-ish array strings stored in the CSV: ["a", "b"]
        public static string[] ParseArray(string s)
        {
            List<string> outp = new List<string>();
            if (string.IsNullOrEmpty(s)) return outp.ToArray();
            int i = s.IndexOf('[');
            if (i < 0) return outp.ToArray();
            i++;
            StringBuilder sb = new StringBuilder();
            bool inStr = false;
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr)
                {
                    if (c == '\\' && i + 1 < s.Length)
                    {
                        char n = s[++i];
                        if (n == 'n') sb.Append(' ');
                        else if (n == 't') sb.Append(' ');
                        else if (n == 'u' && i + 4 < s.Length)
                        {
                            int code = int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber);
                            sb.Append((char)code); i += 4;
                        }
                        else sb.Append(n);
                    }
                    else if (c == '"') { inStr = false; outp.Add(sb.ToString().Trim()); sb.Length = 0; }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') inStr = true;
                    else if (c == ']') break;
                }
            }
            return outp.ToArray();
        }

        // ------------------------------------------------- second source (archive2.zip)
        // recipes.csv there stores ingredients as one comma-joined line:
        //   "2 stalks celery, chopped, 1  carrot, diced,   salt and pepper to taste"
        // A new ingredient starts where a comma is followed by an amount, or by the run of
        // spaces the file leaves where an amount is missing. "celery, chopped" stays whole.
        // (¼-¾ and ⅐-⅞ are the fraction characters: one-half, one-third, ...)
        static Regex IngSplit = new Regex(@",(?= ?[\d¼-¾⅐-⅞]|  +)", RegexOptions.Compiled);

        public static string[] SplitIngredientLine(string s)
        {
            List<string> outp = new List<string>();
            if (string.IsNullOrEmpty(s)) return outp.ToArray();
            foreach (string part in IngSplit.Split(s))
            {
                string p = Regex.Replace(part, @"\s+", " ").Trim();
                if (p.Length > 0) outp.Add(p);
            }
            return outp.ToArray();
        }

        public static string[] SplitLines(string s)
        {
            List<string> outp = new List<string>();
            if (string.IsNullOrEmpty(s)) return outp.ToArray();
            foreach (string line in s.Split('\n'))
            {
                string p = line.Trim();
                if (p.Length > 0) outp.Add(p);
            }
            return outp.ToArray();
        }

        /// <summary>Python-style list of strings, either quote style: ['a', "b's"].</summary>
        public static string[] ParsePyList(string s)
        {
            List<string> outp = new List<string>();
            if (string.IsNullOrEmpty(s)) return outp.ToArray();
            StringBuilder sb = new StringBuilder();
            char quote = '\0';
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (quote != '\0')
                {
                    if (c == '\\' && i + 1 < s.Length) sb.Append(s[++i]);
                    else if (c == quote) { quote = '\0'; outp.Add(sb.ToString().Trim()); sb.Length = 0; }
                    else sb.Append(c);
                }
                else if (c == '\'' || c == '"') quote = c;
            }
            return outp.ToArray();
        }

        /// <summary>test_recipes.csv ingredients: [{'quantity': '2', 'unit': 'cups', 'name': 'rice'}, ...]
        /// The flat list alternates key, value, key, value, so each dict becomes "2 cups rice".</summary>
        public static string[] ParsePyIngredients(string s)
        {
            List<string> outp = new List<string>();
            string[] tok = ParsePyList(s);
            string qty = "", unit = "";
            for (int i = 0; i + 1 < tok.Length; i += 2)
            {
                if (tok[i] == "quantity") qty = tok[i + 1];
                else if (tok[i] == "unit") unit = tok[i + 1];
                else if (tok[i] == "name")
                {
                    string line = Regex.Replace(qty + " " + unit + " " + tok[i + 1], @"\s+", " ").Trim();
                    if (line.Length > 0) outp.Add(line);
                    qty = ""; unit = "";
                }
            }
            return outp.ToArray();
        }

        /// <summary>"1 hrs 30 mins" -> 90. 0 when blank or unreadable.</summary>
        public static int StatedMinutes(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int total = 0;
            foreach (Match m in Regex.Matches(s.ToLowerInvariant(), @"(\d+)\s*(day|hr|hour|min)"))
            {
                int v = int.Parse(m.Groups[1].Value);
                string u = m.Groups[2].Value;
                total += u == "day" ? v * 1440 : (u == "min" ? v : v * 60);
            }
            return total;
        }

        // ------------------------------------------------------------ time model
        static Regex TimeRx = new Regex(
            @"(\d+(?:\.\d+)?(?:\s+\d/\d)?|\d/\d)\s*(?:(?:to|-|–|—|or|and)\s*(\d+(?:\.\d+)?(?:\s+\d/\d)?|\d/\d)\s*)?(seconds?|secs?|minutes?|mins?|hours?|hrs?)",
            RegexOptions.Compiled);

        static double Num(string s)
        {
            s = s.Trim();
            double total = 0;
            string[] parts = s.Split(' ');
            foreach (string p in parts)
            {
                if (p.Length == 0) continue;
                if (p.Contains("/"))
                {
                    string[] f = p.Split('/');
                    double a, b;
                    if (double.TryParse(f[0], NumberStyles.Any, CultureInfo.InvariantCulture, out a) &&
                        double.TryParse(f[1], NumberStyles.Any, CultureInfo.InvariantCulture, out b) && b != 0)
                        total += a / b;
                }
                else
                {
                    double v;
                    if (double.TryParse(p, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) total += v;
                }
            }
            return total;
        }

        /// <summary>Elapsed cooking minutes stated in the directions. -1 = disqualified (make-ahead / very long).</summary>
        public static int CookMinutes(string dir, out bool anyStated)
        {
            anyStated = false;
            double total = 0;
            foreach (Match m in TimeRx.Matches(dir))
            {
                double lo = Num(m.Groups[1].Value);
                double hi = m.Groups[2].Success ? Num(m.Groups[2].Value) : lo;
                double v = Math.Max(lo, hi);
                string unit = m.Groups[3].Value;
                if (unit.StartsWith("s")) v = v / 60.0;
                else if (unit.StartsWith("h")) v = v * 60.0;
                // ignore temperature-ish or nonsense
                if (v <= 0) continue;
                if (v > 75) return -1;              // one very long step: not a weeknight dinner
                anyStated = true;
                total += v;
            }
            if (total > 150) return -1;
            return (int)Math.Round(total);
        }

        static string[] MakeAhead = new string[] {
            "overnight", "over night", "slow cooker", "crock pot", "crockpot", "pressure canner",
            "let rise", "until doubled", "proof the dough", "knead", "ferment", "cure for", "brine overnight",
            "8 hours", "6 hours", "12 hours", "24 hours", "several hours", "at least 2 hours", "at least 3 hours",
            "at least 4 hours", "2 to 3 hours", "3 to 4 hours", "1 to 2 hours", "day ahead", "day before",
            "freeze until", "until frozen", "ice cream maker", "dehydrator"
        };
        static Regex ChillLong = new Regex(
            @"(refrigerat\w*|chill\w*|marinat\w*|let\s+stand|rest|soak\w*|freez\w*)[^.]{0,60}?(\d+(?:\.\d+)?)\s*(hours?|hrs?)",
            RegexOptions.Compiled);
        static Regex ChillLongMin = new Regex(
            @"(refrigerat\w*|chill\w*|marinat\w*|soak\w*)[^.]{0,60}?(\d{2,3})\s*(minutes?|mins?)",
            RegexOptions.Compiled);

        public static bool IsMakeAhead(string dir)
        {
            for (int i = 0; i < MakeAhead.Length; i++) if (dir.Contains(MakeAhead[i])) return true;
            if (ChillLong.IsMatch(dir)) return true;
            foreach (Match m in ChillLongMin.Matches(dir))
            {
                double v = Num(m.Groups[2].Value);
                if (v >= 45) return true;
            }
            return false;
        }

        static string[] KnifeVerbs = new string[] {
            "chop", "chopped", "dice", "diced", "mince", "minced", "slice", "sliced", "peel", "peeled",
            "grate", "grated", "shred", "shredded", "cube", "cubed", "julienne", "trim", "trimmed",
            "quarter", "halve", "cut into", "seed", "core", "devein", "crush"
        };

        static string[] PantryStaples = new string[] {
            "salt", "pepper", "black pepper", "water", "olive oil", "vegetable oil", "canola oil", "oil",
            "cooking spray", "butter", "sugar", "brown sugar", "flour", "baking powder", "baking soda",
            "cornstarch", "vanilla", "cumin", "paprika", "oregano", "thyme", "basil", "rosemary", "sage",
            "garlic powder", "onion powder", "chili powder", "cayenne", "red pepper flakes", "cinnamon",
            "nutmeg", "bay leaf", "bay leaves", "soy sauce", "vinegar", "white vinegar", "balsamic vinegar",
            "broth", "stock", "ketchup", "mustard", "mayonnaise", "worcestershire sauce", "honey",
            "italian seasoning", "seasoning", "hot sauce", "sesame oil", "cooking wine", "bouillon",
            "coriander", "turmeric", "dill", "parsley flakes", "chives", "celery salt", "garlic salt"
        };

        /// <summary>Ingredients that actually need handling; salt and spices do not.</summary>
        public static int RealIngredients(string[] ingredients)
        {
            int real = 0;
            for (int i = 0; i < ingredients.Length; i++)
            {
                string low = ingredients[i].ToLowerInvariant();
                bool staple = false;
                for (int j = 0; j < PantryStaples.Length; j++)
                {
                    if (Has(low, PantryStaples[j])) { staple = true; break; }
                }
                if (!staple) real++;
            }
            return real;
        }

        public static int KnifeWork(string dir, string ingText)
        {
            int knife = 0;
            for (int i = 0; i < KnifeVerbs.Length; i++)
                if (dir.Contains(KnifeVerbs[i]) || ingText.Contains(KnifeVerbs[i])) knife++;
            return knife;
        }

        public static int PrepMinutes(string[] ingredients, string dir, string ingText)
        {
            int real = RealIngredients(ingredients);
            int knife = 0;
            for (int i = 0; i < KnifeVerbs.Length; i++)
                if (dir.Contains(KnifeVerbs[i]) || ingText.Contains(KnifeVerbs[i])) knife++;
            double p = 4 + 1.0 * real + 1.2 * Math.Min(knife, 8);
            if (p < 5) p = 5;
            if (p > 35) p = 35;
            return (int)Math.Round(p);
        }

        // -------------------------------------------------------- disqualifiers
        // Anywhere in the title => never a dinner.
        public static string[] NotDinner = new string[] {
            "cake","cupcake","cookie","brownie","blondie","frosting","icing","fudge","candy","toffee","truffle",
            "pie crust","cheesecake","pudding","custard","flan","mousse","ice cream","gelato","sorbet","sherbet",
            "popsicle","smoothie","milkshake","cocktail","martini","margarita","mojito","daiquiri","sangria",
            "lemonade","iced tea","latte","cappuccino","eggnog","hot chocolate","cobbler","crumble",
            "shortcake","trifle","tiramisu","macaroon","macaron","biscotti","scone","muffin","donut","doughnut",
            "danish","croissant","cinnamon roll","sticky bun","coffee cake","banana bread","zucchini bread",
            "pancake","waffle","french toast","crepe","granola","oatmeal","porridge","overnight oats","parfait",
            "marmalade","chutney","canning","spice rub","spice mix","seasoning mix","compound butter",
            "cheese ball","snack mix","trail mix","fudgy","tart shell","pastry cream",
            "whipped cream","caramel","smoothie bowl","energy ball","protein ball","fruit salad","ambrosia",
            "jello","gelatin","cornbread","dinner roll","breadstick","pizza dough","pie dough","bread dough",
            "bread machine","sourdough starter","kombucha","liqueur","moonshine",
            "baby food","dog treat","playdough","brine","mulled","frappe","spritzer","mocktail",
            "chocolate","cocoa","marshmallow","butterscotch","praline","fritter","churro","beignet",
            "meringue","ganache","curd","streusel","turnover","sweet roll","deviled egg",
            "smore","s'more","cream puff","eclair","kolache","rugelach","halva","baklava","funnel"
        };

        // Sweet baking signals: two or more of these with almost no savoury anchor = dessert.
        static string[] Sweeteners = new string[] {
            "white sugar","granulated sugar","brown sugar","powdered sugar","confectioners sugar",
            "corn syrup","maple syrup","molasses","chocolate chip","cocoa powder","sweetened condensed milk",
            "marshmallow","vanilla extract","almond extract","frosting","food coloring","jam","jelly",
            "apple pie filling","pie filling","caramel","honey","icing sugar","cake mix","pudding mix"
        };
        static string[] SavoryAnchors = new string[] {
            "onion","garlic","black pepper","pepper","broth","stock","soy sauce","tomato","tomato sauce",
            "cumin","chili powder","paprika","oregano","basil","thyme","rosemary","celery","carrot",
            "bell pepper","mushroom","olive oil","vinegar","mustard","worcestershire","salsa","cilantro",
            "chicken","beef","pork","turkey","fish","shrimp","bacon","sausage","ham","bean","lentil",
            "potato","rice","pasta","noodle","spinach","broccoli","zucchini","cabbage","lettuce","cheese"
        };

        // The recipe is only a staple or a nibble unless it carries real protein.
        static string[] StapleHead = new string[] {
            "tortilla","bread","roll","bun","dough","crust","pita","naan","biscuit","cracker","crepe",
            "pastry","noodle","pasta","rice","polenta","grits","quinoa","couscous","chip","crisps",
            "fries","wedge","tot","ring","stick","popper","crouton","bagel","toast","puff","ball",
            "batter","pancake","waffle","dumpling","pierogi","wrapper","shell","cup","bite","muffin",
            // vegetable and dairy sides: a dinner needs more than a bowl of these
            "potato","potatoes","zucchini","romaine","asparagus","bean","broccoli","carrot","corn",
            "cauliflower","squash","green","sprout","pea","onion","oil","fondue","slaw","dip",
            "mushroom","tomato","cucumber","beet","parsnip","turnip","artichoke","okra","leek"
        };

        // Head phrases that mark a side dish even though the last word alone looks fine.
        static string[] SideHeadPhrases = new string[] {
            "mashed potato","cream cheese","dipping oil","dipping sauce","hash brown","garlic bread",
            "french fries","onion ring","hearts of romaine","side salad","cheese ball","compound butter"
        };
        static string[] RealProtein = new string[] {
            "chicken","beef","ground beef","steak","pork","ham","bacon","sausage","kielbasa","chorizo",
            "turkey","lamb","shrimp","prawn","salmon","tuna","cod","tilapia","halibut","haddock","trout",
            "sardine","mackerel","fish","crab","scallop","lobster","clam","mussel","tofu","tempeh",
            "black bean","pinto bean","kidney bean","white bean","cannellini","chickpea","garbanzo",
            "lentil","refried bean","edamame","rotisserie chicken","ground turkey","pepperoni","prosciutto"
        };

        static string[] OddProcess = new string[] {
            "sausage casing","meat grinder","grinder attachment","sausage stuffer","hog casing",
            "curing salt","prague powder","smoker","cold smoke","canning jar","water bath canner",
            "pressure canner","sterilize the jars","ice cream maker"
        };

        // Only a non-dinner when the title is basically just this thing
        // ("Fresh Salsa" is out, "Fish Tacos with Corn Salsa" stays in).
        public static string[] SideOnly = new string[] {
            "salsa","dip","pesto","aioli","tzatziki","hummus","guacamole","relish","pickle","pickled","jam",
            "jelly","preserve","syrup","gravy","roux","sauce","dressing","vinaigrette","marinade",
            "stock","broth","bruschetta","crostini","canape","cracker","rub","glaze","mayonnaise",
            "ketchup","mustard","spread","topping","filling","batter","icing","seasoning","garnish","puree"
        };

        static string[] Splitters = new string[] { " with ", " in a ", " in ", " over ", " served ",
            " topped ", " on a bed", " without ", " for " };

        /// <summary>The part of the title that names the dish, before any "with ..." tail.</summary>
        static string HeadPhrase(string titleLower)
        {
            string head = titleLower;
            for (int i = 0; i < Splitters.Length; i++)
            {
                int k = head.IndexOf(Splitters[i]);
                if (k > 0) head = head.Substring(0, k);
            }
            head = Regex.Replace(head, @"[^a-z ]", " ");
            return Regex.Replace(head, @"\s+", " ").Trim();
        }

        static string HeadNoun(string titleLower)
        {
            string head = titleLower;
            for (int i = 0; i < Splitters.Length; i++)
            {
                int k = head.IndexOf(Splitters[i]);
                if (k > 0) head = head.Substring(0, k);
            }
            head = Regex.Replace(head, @"[^a-z ]", " ");
            head = Regex.Replace(head, @"\s+", " ").Trim();
            if (head.Length == 0) return "";
            string[] w = head.Split(' ');
            return w[w.Length - 1];
        }

        static bool IsStapleHead(string titleLower)
        {
            string head = HeadPhrase(titleLower);
            for (int i = 0; i < SideHeadPhrases.Length; i++) if (Has(head, SideHeadPhrases[i])) return true;
            string last = HeadNoun(titleLower);
            if (last.Length == 0) return false;
            for (int i = 0; i < StapleHead.Length; i++) if (Has(last, StapleHead[i])) return true;
            return false;
        }

        /// <summary>True when the dish the title names is a condiment, not a meal.
        /// "Fresh Tomato Salsa" is out; "Fish Tacos with Corn Salsa" keeps its head noun (tacos).</summary>
        static bool IsSideOnly(string titleLower)
        {
            string last = HeadNoun(titleLower);
            if (last.Length == 0) return false;
            for (int i = 0; i < SideOnly.Length; i++) if (Has(last, SideOnly[i])) return true;
            return false;
        }

        public static string[] CategoryBlock = new string[] {
            "Cakes","Cookies","Desserts","Pies","Christmas Cookies","Frostings And Icings","Cocktails",
            "Breads","Yeast Breads","Muffins","Pancakes","Canning And Preserving","Drinks","Candy",
            "Cupcakes","Brownies And Bars","Ice Cream","Frozen Desserts","Cheesecake","Beverages",
            "Quick Bread","Biscuits","Scones","Doughnuts","Pastries","Jams And Jellies","Smoothies",
            "Cookies For Kids","Trifle","Fruit Desserts","Puddings","Custards And Puddings","Snacks",
            "Salad Dressings","Condiments","Sauces And Condiments","Spices And Seasonings","Fruit Salads",
            "Appetizers And Snacks","Baking","Dessert Sauces","Herbs And Spices",
            // top-level sections as archive2.zip names them
            "Drinks Recipes","Bread","Quick Bread Recipes"
        };

        // Ingredients a home cook in North America cannot reliably buy at a normal supermarket.
        public static string[] HardToFind = new string[] {
            "galangal","kaffir lime","curry leaf","curry leaves","pandan","ube","calamansi","yuzu","shiso",
            "mitsuba","katsuobushi","bonito flake","kombu","dashi","shrimp paste","belacan","epazote",
            "huitlacoche","annatto","achiote","preserved lemon","pomegranate molasses","black garlic",
            "verjus","guanciale","nduja","lardo","bottarga","samphire","sea bean","elderflower","sorrel",
            "fiddlehead","ramp","morel","fresh truffle","truffle oil","black truffle","white truffle",
            "quail","pheasant","rabbit","venison","goat meat","wild boar","squab","goose","duck",
            "elk","moose","backstrap","antelope","caribou","ostrich","emu","kangaroo","bear meat",
            "sea urchin","uni","caviar","escargot","monkfish","skate wing","turbot","branzino","bottarga",
            "salt cod","bacalao","conch","abalone","geoduck","sea cucumber","century egg","natto",
            "fenugreek leaf","methi","asafoetida","hing","ajwain","nigella","amchur","kokum","jaggery",
            "banana blossom","durian","jackfruit","longan","rambutan","cherimoya","feijoa","salak",
            "nopales","nopalitos","cactus paddle","cactus","chayote","tomatillo husk","mamey",
            "young coconut","palm sugar","coconut vinegar","kecap manis","laksa paste","gochujang paste",
            "doubanjiang","chinkiang","shaoxing","mirin substitute","sansho","togarashi","furikake",
            "ras el hanout","berbere","dukkah","urfa","espelette",
            "lardons","speck","culatello","morcilla","chorizo iberico","jamon",
            "queso de bola","paneer","quark","clotted cream","raclette","taleggio",
            "fresh yeast","malt powder","lye","agar","carrageenan","xanthan","transglutaminase",
            "sweetbread","tripe","kidney","tongue","brain","gizzard","chitterling","head cheese",
            "oxtail","marrow bone","trotter","blood sausage","haggis","pate","foie gras"
        };

        static string[] OffalAndOdd = new string[] {
            "tripe","sweetbread","gizzard","chitterling","tongue","brains","kidneys","snout","trotter",
            "blood sausage","head cheese","haggis","frog leg","alligator","turtle","snail","cricket"
        };

        // ------------------------------------------------------- main-dish test
        static string[] Proteins = new string[] {
            "chicken","chicken breast","chicken thigh","rotisserie chicken","turkey","ground turkey",
            "beef","ground beef","steak","sirloin","flank steak","skirt steak","beef strips","roast beef",
            "pork","pork chop","pork loin","pork tenderloin","ground pork","ham","prosciutto","bacon",
            "sausage","italian sausage","kielbasa","chorizo","hot dog","bratwurst","pepperoni","salami",
            "lamb","ground lamb","shrimp","prawn","salmon","tuna","cod","tilapia","halibut","haddock",
            "trout","mahi mahi","snapper","sole","catfish","pollock","sardine","mackerel","anchovy",
            "scallop","crab","crabmeat","lobster","clam","mussel","calamari","squid","fish fillet","fish",
            "tofu","tempeh","seitan","egg","eggs","black bean","pinto bean","kidney bean","white bean",
            "cannellini","navy bean","garbanzo","chickpea","lentil","split pea","edamame","refried bean",
            "ricotta","mozzarella","cheddar","feta","goat cheese","parmesan","cheese"
        };

        static string[] BaseCarbs = new string[] {
            "pasta","spaghetti","penne","rigatoni","fettuccine","linguine","macaroni","noodle","ramen",
            "udon","soba","rice noodle","lasagna","ravioli","tortellini","gnocchi","orzo","couscous",
            "rice","brown rice","jasmine rice","basmati","risotto","arborio","quinoa","farro","barley",
            "bulgur","polenta","grits","tortilla","bread","baguette","roll","bun","pita","naan","flatbread",
            "pizza crust","english muffin","potato","sweet potato","hash brown","wonton wrapper"
        };

        // ------------------------------------------------------- classification
        class Fam { public string Name; public string[] Keys; public int Pri; public Fam(string n, int p, string[] k) { Name = n; Pri = p; Keys = k; } }

        static Fam[] Families = new Fam[] {
            new Fam("Soup", 10, new string[]{"soup","chowder","bisque","broth bowl","ramen","pho","minestrone",
                "tortilla soup","egg drop","miso soup","gazpacho","borscht","avgolemono","noodle soup","stracciatella"}),
            new Fam("Stew & Chili", 11, new string[]{"chili","stew","goulash","gumbo","jambalaya","ragout",
                "cacciatore","bourguignon","tagine","pot pie filling","sloppy","braise","braised"}),
            new Fam("Pasta", 20, new string[]{"pasta","spaghetti","penne","rigatoni","fettuccine","linguine",
                "macaroni","noodle","lasagna","ravioli","tortellini","gnocchi","orzo","carbonara","alfredo",
                "primavera","bolognese","lo mein","pad thai","chow mein","mac and cheese","ziti","farfalle",
                "angel hair","bucatini","cavatappi","shells","rotini","vermicelli","udon","soba","ramen noodle"}),
            new Fam("Rice", 21, new string[]{"rice","risotto","paella","fried rice","pilaf","biryani","jambalaya",
                "rice bowl","burrito bowl","poke bowl","arroz","congee","sushi","rice casserole"}),
            new Fam("Bread & Sandwich", 22, new string[]{"sandwich","sub","hoagie","grinder","panini","melt",
                "burger","cheeseburger","hamburger","sloppy joe","wrap","taco","burrito","quesadilla",
                "enchilada","tostada","pizza","flatbread","calzone","stromboli","pita pocket","gyro",
                "shawarma","bruschetta toast","toast","french dip","sliders","banh mi","reuben","blt",
                "grilled cheese","pinwheel","pigs in a blanket","hot dog","bratwurst bun","empanada",
                "pastry pocket","hand pie","strudel savory","pot pie","tart savory","galette savory"}),
            new Fam("Main Salad", 23, new string[]{"salad"}),
            new Fam("Grain Bowl", 24, new string[]{"quinoa","couscous","farro","barley","bulgur","tabbouleh",
                "grain bowl","buddha bowl","polenta","grits","millet","freekeh"}),
            new Fam("Potato", 25, new string[]{"potato","potatoes","hash","shepherd","cottage pie",
                "baked potato","gnocchi potato","colcannon","rosti","home fries"}),
            new Fam("Eggs", 26, new string[]{"frittata","omelet","omelette","shakshuka","quiche","strata",
                "scrambled egg","egg bake","poached egg","huevos","egg foo","souffle savory","egg salad"}),
            new Fam("Stir-fry & Skillet", 30, new string[]{"stir fry","stir-fry","skillet","saute","sauté",
                "wok","teriyaki","sweet and sour","kung pao","szechuan","hibachi","fajita","goulash skillet",
                "hash skillet","one pan","one-pan","scramble"}),
            new Fam("Casserole & Bake", 31, new string[]{"casserole","bake","baked","gratin","au gratin",
                "hotdish","cobbler savory","shepherds pie","enchilada bake","stuffed shells","manicotti",
                "moussaka","tamale pie","strata savory"}),
            new Fam("Grilled", 32, new string[]{"grill","grilled","barbecue","bbq","kebab","kabob","skewer",
                "satay","souvlaki","yakitori","char","broil","broiled"}),
            new Fam("Roasted & Sheet-Pan", 33, new string[]{"roast","roasted","sheet pan","sheet-pan",
                "oven baked","oven-baked","tray bake","pan roasted"}),
            new Fam("Fish & Seafood Plate", 34, new string[]{"salmon","tuna steak","cod","tilapia","halibut",
                "haddock","trout","shrimp","scallop","crab cake","fish","seafood","mahi","snapper","sole",
                "catfish","ceviche","scampi"}),
            new Fam("Meat & Veggies", 40, new string[]{"chicken","beef","pork","steak","turkey","lamb","chop",
                "cutlet","meatball","meatloaf","tenderloin","schnitzel","piccata","marsala","parmigiana",
                "stuffed pepper","stuffed zucchini","cabbage roll","lettuce wrap","kofta","stroganoff"})
        };

        static string[] SummerKeys = new string[] {
            "grill","grilled","bbq","barbecue","skewer","kebab","kabob","no cook","no-cook","chilled","cold",
            "fresh tomato","cherry tomato","corn","zucchini","summer squash","cucumber","bell pepper",
            "basil","cilantro","lime","lemon","watermelon","peach","berry","strawberry","blueberry",
            "avocado","mango","arugula","spinach salad","coleslaw","slaw","gazpacho","ceviche","poke",
            "shrimp salad","pasta salad","picnic","light","salsa fresca","pico de gallo","mint","radish",
            "green bean","asparagus","snap pea","peach salsa","feta","greek salad","caprese","taco",
            "wrap","sandwich","grilled chicken","stir fry","skillet"
        };

        static string[] WinterKeys = new string[] {
            "soup","stew","chili","chowder","bisque","casserole","roast","roasted","braise","braised",
            "bake","baked","gratin","potato","sweet potato","butternut","squash","pumpkin","parsnip",
            "turnip","rutabaga","beet","cabbage","kale","collard","brussels sprout","carrot","leek",
            "onion soup","mushroom","barley","lentil","split pea","bean soup","dumpling","noodle soup",
            "pot pie","shepherd","meatloaf","pot roast","gravy","comfort","hearty","warm","cozy","stuffed",
            "beef stew","cinnamon","clove","nutmeg","sage","rosemary","thyme","cheesy","creamy","melted"
        };

        class Bone { public string Label; public int Pts; public string[] Keys; public Bone(string l, int p, string[] k) { Label = l; Pts = p; Keys = k; } }

        static Bone[] BoneFoods = new Bone[] {
            new Bone("canned salmon (with bones)", 26, new string[]{"canned salmon","salmon with bones"}),
            new Bone("sardines", 26, new string[]{"sardine"}),
            new Bone("plain yogurt", 20, new string[]{"yogurt","greek yogurt","kefir"}),
            new Bone("milk", 18, new string[]{"milk","evaporated milk","buttermilk","skim milk","whole milk"}),
            new Bone("ricotta", 18, new string[]{"ricotta"}),
            new Bone("parmesan", 16, new string[]{"parmesan","parmigiano","pecorino","romano cheese"}),
            new Bone("cheese", 13, new string[]{"cheddar","mozzarella","swiss cheese","provolone","gouda",
                "monterey jack","colby","feta","goat cheese","cream cheese","shredded cheese","cheese"}),
            new Bone("tofu", 20, new string[]{"tofu","tempeh","edamame"}),
            new Bone("collard / turnip greens", 20, new string[]{"collard","turnip green","mustard green"}),
            new Bone("kale", 18, new string[]{"kale"}),
            new Bone("bok choy", 18, new string[]{"bok choy","napa cabbage"}),
            new Bone("sesame / tahini", 15, new string[]{"sesame seed","tahini"}),
            new Bone("almonds", 14, new string[]{"almond","almond milk","sliced almonds"}),
            new Bone("white beans", 14, new string[]{"white bean","cannellini","navy bean","great northern"}),
            new Bone("chia / flax", 10, new string[]{"chia","flaxseed","flax seed"}),
            new Bone("figs", 10, new string[]{"fig","dried fig"}),
            new Bone("salmon", 20, new string[]{"salmon","salmon fillet"}),
            new Bone("mackerel / herring", 20, new string[]{"mackerel","herring"}),
            new Bone("trout", 18, new string[]{"trout"}),
            new Bone("tuna", 14, new string[]{"tuna"}),
            new Bone("shrimp", 10, new string[]{"shrimp","prawn"}),
            new Bone("eggs", 12, new string[]{"egg","eggs"}),
            new Bone("broccoli", 14, new string[]{"broccoli","broccolini","broccoli rabe"}),
            new Bone("spinach", 11, new string[]{"spinach"}),
            new Bone("swiss chard", 12, new string[]{"swiss chard","chard"}),
            new Bone("brussels sprouts", 11, new string[]{"brussels sprout"}),
            new Bone("cabbage", 8, new string[]{"cabbage","savoy cabbage"}),
            new Bone("okra", 9, new string[]{"okra"}),
            new Bone("chickpeas", 11, new string[]{"chickpea","garbanzo"}),
            new Bone("lentils", 11, new string[]{"lentil"}),
            new Bone("black / pinto beans", 9, new string[]{"black bean","pinto bean","kidney bean","refried bean"}),
            new Bone("quinoa", 9, new string[]{"quinoa"}),
            new Bone("mushrooms (vitamin D)", 8, new string[]{"mushroom","cremini","portobello","shiitake"}),
            new Bone("walnuts / pumpkin seeds", 9, new string[]{"walnut","pumpkin seed","pepita","sunflower seed"}),
            new Bone("sweet potato", 8, new string[]{"sweet potato"}),
            new Bone("oranges", 7, new string[]{"orange","mandarin","clementine"}),
            new Bone("prunes", 12, new string[]{"prune","dried plum"}),
            new Bone("lean poultry (protein)", 8, new string[]{"chicken breast","chicken thigh","chicken",
                "turkey","ground turkey"}),
            new Bone("lean pork / beef (protein)", 6, new string[]{"pork loin","pork tenderloin","pork chop",
                "sirloin","lean ground beef","beef","steak"}),
            new Bone("white fish (protein)", 12, new string[]{"cod","tilapia","halibut","haddock","pollock","sole","snapper"}),
            new Bone("fortified soy milk", 14, new string[]{"soy milk","soymilk","fortified almond milk"}),
            new Bone("nutritional yeast", 7, new string[]{"nutritional yeast"}),
            new Bone("bell peppers / tomatoes (vitamin C)", 5, new string[]{"bell pepper","tomato","red pepper"}),
            new Bone("olive oil", 4, new string[]{"olive oil"})
        };

        static string[] BonePenalty = new string[] {
            "cola","soda","soft drink","root beer","vodka","whiskey","bourbon","rum","tequila","gin",
            "hot dog","bologna","salami","pepperoni","bacon","canned soup","instant ramen","shortening",
            "lard","deep fry","deep-fry","fried","margarine","corn syrup","marshmallow","potato chip",
            "processed cheese","velveeta","spam","vienna sausage","msg"
        };

        static string[] FriedKeys = new string[] { "deep fry", "deep-fry", "deep fried", "deep-fried", "hot oil", "oil for frying", "inches of oil", "fry until golden brown" };

        // Real heat in the pot. The archive's own "spicy" taste label is unreliable in both
        // directions (it calls corn on the cob spicy, and misses plenty of chilli), so the
        // label and this list are both used, and either one is enough to drop a recipe.
        // Deliberately absent: chilli powder, paprika, ancho, poblano, taco seasoning and
        // jarred salsa, which are mild in North American cooking.
        public static string[] HotStuff = new string[] {
            "cayenne","red pepper flake","crushed red pepper","chili flake","chile flake","hot pepper",
            "jalapeno","jalapeños","serrano","habanero","scotch bonnet","ghost pepper","thai chile",
            "thai chili","bird's eye","birds eye chile","chipotle","adobo sauce","chile de arbol",
            "sriracha","sambal","sambal oelek","chili paste","chile paste","chili garlic sauce",
            "chili oil","chile oil","hot sauce","tabasco","frank's red hot","buffalo sauce","wing sauce",
            "harissa","gochugaru","gochujang","kimchi","horseradish","wasabi","pepper jack","hot italian sausage",
            "andouille","chorizo","spicy","hot chile","hot chili","arrabbiata","diavolo","diablo","piri piri",
            "peri peri","jerk seasoning","jerk paste","cajun seasoning","creole seasoning","blackened seasoning",
            "old bay hot","chili crisp","calabrian","pepperoncini","giardiniera","rotel","hatch chile",
            "green chile","poblano hot","fresno","cherry pepper","banana pepper hot","szechuan peppercorn",
            "five alarm","firecracker","volcano","atomic","hot honey","nashville hot","buffalo chicken",
            "buffalo dip","chili crunch","spicy mayo","bang bang","dynamite sauce","wasabi mayo"
        };

        /// <summary>When false, only real heat in the ingredients drops a recipe, and the
        /// archive's own (noisy) "spicy" taste label is ignored.</summary>
        public static bool UseSpicyLabel = true;

        // Fussier moves that make a recipe feel harder than its step count suggests.
        static string[] Fussy = new string[] {
            "fold in","whisk until","stiff peaks","temper","deglaze","reduce by half","reduce until",
            "dredge","breading","bread the","coat in flour then","double boiler","candy thermometer",
            "instant-read thermometer","meat thermometer","blanch","ice bath","puree","blender","food processor",
            "stand mixer","roll out","rolling pin","knead","stuff the","stuffing into","skewer","truss",
            "butterfly","pound to","pound until","julienne","mandoline","strain through","cheesecloth",
            "separate the eggs","emulsify","clarify","caramelize the","constantly stirring","stirring constantly",
            "do not let it","be careful not to","work quickly","in batches","meanwhile","while the","simmer while",
            "flip carefully","transfer to the oven","finish under the broiler","tent with foil","let it rest before"
        };

        static string[] TopTitleStrong = new string[] {
            "award winning","award-winning","blue ribbon","world's best","worlds best","prize winning",
            "prize-winning","famous","hall of fame","state fair winner"
        };
        static string[] TopTitleSoft = new string[] {
            "best","favorite","favourite","perfect","ultimate","secret","grandma","grandmother","nana",
            "copycat","classic","authentic","legendary","go-to","never fail","foolproof","restaurant style",
            "restaurant-style","better than"
        };

        static string[] CuisineKeys = new string[] {
            "Italian|italian,pasta,parmesan,marinara,pesto,risotto,piccata,caprese,bolognese,alfredo,lasagna,gnocchi,bruschetta,prosciutto,mozzarella",
            "Mexican|mexican,taco,burrito,enchilada,quesadilla,fajita,salsa,tortilla,cilantro lime,chipotle,carnitas,pico de gallo,tostada,queso,elote",
            "Asian|asian,soy sauce,stir fry,stir-fry,teriyaki,sesame oil,hoisin,ginger garlic,chinese,japanese,korean,thai,vietnamese,fried rice,lo mein,ramen,miso,sriracha,bok choy",
            "Mediterranean|mediterranean,greek,feta,olive,oregano lemon,tzatziki,hummus,tahini,pita,gyro,souvlaki,chickpea,za'atar,lemon herb",
            "Southern & Cajun|cajun,creole,jambalaya,gumbo,southern,collard,blackened,andouille,grits,hush puppy,dirty rice",
            "French|french,dijon,herbes de provence,ratatouille,coq au,beurre,nicoise,bourguignon,gratin",
            "Middle Eastern|middle eastern,shawarma,kofta,falafel,harissa,sumac,tabbouleh,baba,labneh",
            "American|american,bbq,barbecue,burger,meatloaf,casserole,ranch,buffalo,sloppy joe,pot roast,sheet pan,mac and cheese"
        };

        // ------------------------------------------------------------- pipeline
        public static Recipe Build(string title, string category, string subcategory, string description,
                                   string[] ings, string[] steps, string tastes,
                                   string[] userExclude, out string reject)
        {
            reject = null;
            string tl = title.ToLowerInvariant();
            string ingText = string.Join(" ; ", ings).ToLowerInvariant();
            string dirText = string.Join(" ", steps).ToLowerInvariant();
            string all = tl + " ; " + ingText + " ; " + dirText;
            string titleIng = tl + " ; " + ingText;

            if (ings.Length < 4) { reject = "too-few-ingredients"; return null; }
            if (steps.Length < 1) { reject = "no-steps"; return null; }

            // 1. foods the cook never wants to see -- the blurb counts too, so an unwanted
            //    food never even gets mentioned on a card ("...swear this is eggplant!")
            string scanAll = all + " ; " + (description == null ? "" : description.ToLowerInvariant());
            for (int i = 0; i < userExclude.Length; i++)
                if (Has(scanAll, userExclude[i])) { reject = "excluded:" + userExclude[i]; return null; }

            // 1b. nothing spicy: the archive's label, plus anything with real heat in it
            if (UseSpicyLabel && tastes != null && tastes.ToLowerInvariant().Contains("spicy")) { reject = "spicy-label"; return null; }
            for (int i = 0; i < HotStuff.Length; i++)
                if (Has(scanAll, HotStuff[i])) { reject = "spicy-heat:" + HotStuff[i]; return null; }

            // 2. not a dinner ("English muffin tuna melt" is a sandwich, not a muffin)
            string tlScan = tl.Replace("english muffin", "roll").Replace("waffle fries", "fries");
            for (int i = 0; i < NotDinner.Length; i++)
                if (Has(tlScan, NotDinner[i])) { reject = "not-dinner:" + NotDinner[i]; return null; }
            if (IsSideOnly(tl)) { reject = "side-or-condiment"; return null; }
            for (int i = 0; i < CategoryBlock.Length; i++)
                if (category.Equals(CategoryBlock[i], StringComparison.OrdinalIgnoreCase)) { reject = "cat:" + category; return null; }

            // sweet-leaning guard: dessert titles are not always obvious ("No Cholesterol Chocolate Chip")
            int sweet = CountAny(ingText, Sweeteners);
            int savory = CountAny(ingText, SavoryAnchors);
            // a cup of chicken broth doesn't make rice a chicken dinner
            bool realProtein = HasAny(Regex.Replace(titleIng, @"(chicken|beef|turkey|fish|seafood|shrimp) (broth|stock|bouillon|base)", "$2"), RealProtein);
            if (sweet >= 2 && savory <= 2 && !realProtein) { reject = "baked-good"; return null; }
            // honey-and-fruit on bread ("Creamy Kiwi Sandwich") is a snack, not dinner
            if (sweet >= 1 && savory <= 1 && !realProtein) { reject = "sweet-snack"; return null; }
            if (Has(ingText, "all-purpose flour") && sweet >= 1 && savory <= 3 && !realProtein)
            { reject = "baked-good"; return null; }

            // a staple or a nibble rather than a dinner ("Tortillas II", "Bagel Chips")
            if (!realProtein && IsStapleHead(tl)) { reject = "staple-only"; return null; }

            // butchery / canning / smoking projects
            for (int i = 0; i < OddProcess.Length; i++)
                if (dirText.Contains(OddProcess[i])) { reject = "odd-process:" + OddProcess[i]; return null; }

            // 3. hard to find in North America
            for (int i = 0; i < HardToFind.Length; i++)
                if (Has(titleIng, HardToFind[i])) { reject = "hard-to-find:" + HardToFind[i]; return null; }
            for (int i = 0; i < OffalAndOdd.Length; i++)
                if (Has(titleIng, OffalAndOdd[i])) { reject = "offal:" + OffalAndOdd[i]; return null; }

            // 4. must read as a real main course
            bool hasProtein = HasAny(titleIng, Proteins);
            bool hasCarb = HasAny(titleIng, BaseCarbs);
            if (!hasProtein && !hasCarb) { reject = "not-a-main"; return null; }

            // 5. timing
            if (IsMakeAhead(dirText)) { reject = "make-ahead"; return null; }
            bool stated;
            int cook = CookMinutes(dirText, out stated);
            if (cook < 0) { reject = "too-long-step"; return null; }
            bool noCook = !HasAny(dirText, new string[] { "cook", "bake", "boil", "simmer", "fry", "saute",
                "sauté", "grill", "roast", "broil", "heat", "microwave", "steam", "preheat", "brown", "sear", "toast" });
            if (!stated && !noCook) { reject = "no-stated-time"; return null; }
            int prep = PrepMinutes(ings, dirText, ingText);
            int total = prep + cook;
            if (total > 45) { reject = "over-45"; return null; }

            Recipe r = new Recipe();
            r.Title = title; r.Category = category; r.Description = description;
            r.Ingredients = ings; r.Steps = steps;
            r.IngredientText = ingText; r.DirectionsText = dirText;
            r.PrepMin = prep; r.CookMin = cook; r.TotalMin = total;

            // 6. dish family -- what the title calls the dish wins; the method is the fallback
            int bestPri = int.MaxValue;
            for (int i = 0; i < Families.Length; i++)
            {
                Fam f = Families[i];
                if (HasAny(tl, f.Keys))
                {
                    if (!r.Bases.Contains(f.Name)) r.Bases.Add(f.Name);
                    if (f.Pri < bestPri) { bestPri = f.Pri; r.Base = f.Name; }
                }
            }
            // a salad or slaw is a salad, whatever else the title mentions
            if (Has(tl, "salad") || Has(tl, "slaw")) r.Base = "Main Salad";
            if (r.Base == null)
            {
                for (int i = 0; i < Families.Length; i++)
                {
                    Fam f = Families[i];
                    bool hit = false;
                    if (f.Name == "Pasta" || f.Name == "Rice" || f.Name == "Potato" || f.Name == "Grain Bowl")
                        hit = HasAny(ingText, f.Keys) && HasAny(dirText, f.Keys);
                    else if (f.Name == "Grilled" || f.Name == "Casserole & Bake" ||
                             f.Name == "Roasted & Sheet-Pan" || f.Name == "Stir-fry & Skillet")
                        hit = HasAny(dirText, f.Keys);
                    else if (f.Name == "Bread & Sandwich" || f.Name == "Eggs" ||
                             f.Name == "Fish & Seafood Plate" || f.Name == "Meat & Veggies")
                        hit = HasAny(ingText, f.Keys);
                    if (hit)
                    {
                        if (!r.Bases.Contains(f.Name)) r.Bases.Add(f.Name);
                        if (f.Pri < bestPri) { bestPri = f.Pri; r.Base = f.Name; }
                    }
                }
            }
            if (r.Base == null) r.Base = "Meat & Veggies";
            if (!r.Bases.Contains(r.Base)) r.Bases.Insert(0, r.Base);
            // a "salad" that is really a side salad is not dinner
            if (r.Base == "Main Salad" && !hasProtein) { reject = "side-salad"; return null; }

            // 7. hot or cold
            bool chilledServe = dirText.Contains("serve chilled") || dirText.Contains("serve cold") ||
                                tl.Contains("cold") || tl.Contains("chilled") || tl.Contains("gazpacho") ||
                                tl.Contains("ceviche");
            r.Temp = (noCook || chilledServe) ? "cold" : "hot";
            // salads and slaws come to the table cold even when something in them was cooked
            if (r.Base == "Main Salad" && !Has(tl, "warm") && !Has(tl, "hot")) r.Temp = "cold";

            // 8. season
            r.SummerScore = CountAny(all, SummerKeys) + (r.Temp == "cold" ? 4 : 0) +
                            (r.Base == "Main Salad" ? 3 : 0) + (r.Base == "Grilled" ? 4 : 0);
            r.WinterScore = CountAny(all, WinterKeys) +
                            (r.Base == "Soup" || r.Base == "Stew & Chili" ? 5 : 0) +
                            (r.Base == "Casserole & Bake" ? 3 : 0) + (r.Base == "Roasted & Sheet-Pan" ? 2 : 0);
            if (r.SummerScore >= r.WinterScore + 3) r.Season = "summer";
            else if (r.WinterScore >= r.SummerScore + 3) r.Season = "winter";
            else r.Season = "any";

            // 9. bone-health score
            int pts = 0;
            for (int i = 0; i < BoneFoods.Length; i++)
            {
                if (HasAny(ingText, BoneFoods[i].Keys))
                {
                    pts += BoneFoods[i].Pts;
                    if (BoneFoods[i].Pts >= 8 && r.BoneWhy.Count < 6) r.BoneWhy.Add(BoneFoods[i].Label);
                }
            }
            int pen = CountAny(all, BonePenalty) * 5;
            double scaled = 100.0 * (1.0 - Math.Exp(-(double)pts / 55.0)) - pen;
            if (scaled < 0) scaled = 0;
            if (scaled > 100) scaled = 100;
            r.Bone = (int)Math.Round(scaled);

            // 10. protein label
            if (HasAny(titleIng, new string[] { "chicken", "rotisserie chicken" })) r.Protein = "Chicken";
            else if (HasAny(titleIng, new string[] { "turkey", "ground turkey" })) r.Protein = "Turkey";
            else if (HasAny(titleIng, new string[] { "salmon", "tuna", "cod", "tilapia", "halibut", "haddock",
                "trout", "mackerel", "sardine", "fish", "snapper", "sole", "catfish", "mahi mahi" })) r.Protein = "Fish";
            else if (HasAny(titleIng, new string[] { "shrimp", "prawn", "scallop", "crab", "lobster", "clam",
                "mussel", "calamari", "squid" })) r.Protein = "Shrimp & Seafood";
            else if (HasAny(titleIng, new string[] { "beef", "ground beef", "steak", "sirloin", "flank steak" })) r.Protein = "Beef";
            else if (HasAny(titleIng, new string[] { "pork", "ham", "bacon", "sausage", "kielbasa", "chorizo", "prosciutto" })) r.Protein = "Pork & Ham";
            else if (HasAny(titleIng, new string[] { "lamb" })) r.Protein = "Lamb";
            else if (HasAny(titleIng, new string[] { "tofu", "tempeh", "seitan" })) r.Protein = "Tofu";
            else if (HasAny(titleIng, new string[] { "bean", "chickpea", "garbanzo", "lentil", "split pea", "edamame" })) r.Protein = "Beans & Lentils";
            else if (HasAny(titleIng, new string[] { "egg", "eggs" })) r.Protein = "Eggs";
            else if (HasAny(titleIng, new string[] { "cheese", "ricotta", "mozzarella", "cheddar", "feta" })) r.Protein = "Cheese";
            else r.Protein = "Vegetables";

            // 11. diet + avoid tags
            bool meat = HasAny(titleIng, new string[]{"chicken","beef","pork","turkey","lamb","bacon","ham",
                "sausage","salmon","tuna","fish","shrimp","crab","clam","anchovy","prosciutto","pepperoni",
                "chorizo","kielbasa","gelatin","broth","stock","steak","cod","tilapia","scallop","lobster","mussel"});
            if (!meat) r.Diet.Add("vegetarian");
            if (!meat && !HasAny(titleIng, new string[]{"cheese","milk","butter","cream","yogurt","egg","eggs",
                "ricotta","parmesan","mozzarella","honey","mayonnaise"})) r.Diet.Add("vegan");
            if (!HasAny(titleIng, new string[]{"milk","cheese","butter","cream","yogurt","ricotta","parmesan",
                "mozzarella","cheddar","feta","half and half","sour cream","ice cream","buttermilk","ghee"}))
                r.Diet.Add("dairy-free");
            if (!HasAny(titleIng, new string[]{"flour","bread","pasta","spaghetti","penne","macaroni","noodle",
                "tortilla","bun","roll","cracker","panko","breadcrumb","soy sauce","couscous","barley","orzo",
                "pita","naan","wonton","dough","beer","cereal","farro","bulgur"}))
                r.Diet.Add("gluten-free");
            if (!HasAny(titleIng, new string[]{"almond","peanut","walnut","pecan","cashew","pistachio","hazelnut",
                "macadamia","nut","nutella","peanut butter","pine nut"}))
                r.Diet.Add("nut-free");
            if (!HasAny(titleIng, new string[]{"pork","bacon","ham","prosciutto","pancetta","sausage","chorizo",
                "pepperoni","salami","lard","kielbasa","bratwurst"}))
                r.Diet.Add("no-pork");
            if (!HasAny(titleIng, new string[]{"shrimp","prawn","crab","lobster","clam","mussel","scallop",
                "oyster","calamari","squid","crawfish"}))
                r.Diet.Add("no-shellfish");

            r.Fried = HasAny(dirText, FriedKeys);

            // 12. cuisine
            r.Cuisine = "Everyday";
            int bestHits = 0;
            for (int i = 0; i < CuisineKeys.Length; i++)
            {
                string[] parts = CuisineKeys[i].Split('|');
                string[] keys = parts[1].Split(',');
                int hits = CountAny(all, keys);
                if (hits > bestHits) { bestHits = hits; r.Cuisine = parts[0]; }
            }
            if (bestHits < 2) r.Cuisine = "Everyday";

            // 13. effort / one-pot
            int vessels = 0;
            string[] v = new string[] { "skillet", "saucepan", "stockpot", "dutch oven", "baking dish",
                "baking sheet", "sheet pan", "casserole dish", "wok", "grill", "air fryer", "slow" };
            for (int i = 0; i < v.Length; i++) if (dirText.Contains(v[i])) vessels++;
            r.OnePot = vessels <= 1;

            // 14. difficulty, worked out from the instructions rather than the archive's
            //     own label (which calls a 5-step air-fryer recipe "hard")
            int realIng = RealIngredients(ings);
            int knifeWork = Math.Min(KnifeWork(dirText, ingText), 8);
            int fussy = CountAny(dirText, Fussy);
            double load = 0.55 * steps.Length
                        + 0.30 * realIng
                        + 0.45 * knifeWork
                        + 1.10 * Math.Max(0, vessels - 1)
                        + 1.55 * fussy
                        + r.TotalMin / 13.0
                        + (r.Fried ? 2.2 : 0);
            if (load <= 4.6) r.Diff = 1;
            else if (load <= 7.0) r.Diff = 2;
            else if (load <= 10.0) r.Diff = 3;
            else if (load <= 14.0) r.Diff = 4;
            else r.Diff = 5;
            string[] dl = new string[] { "Very easy", "Easy", "Medium", "Bit of work", "Most work" };
            r.DiffLabel = dl[r.Diff - 1];
            r.Effort = r.Diff <= 2 ? "easy" : "medium";

            // 15. standing. The archive carries no star ratings or review counts, so this is
            //     the editorial and community signal it does carry: the site's Allstar and
            //     Chef John collections, contest wins, and the way favourites get titled.
            string catAll = ((category == null ? "" : category) + " " + (subcategory == null ? "" : subcategory)).ToLowerInvariant();
            int top = 0;
            if (catAll.Contains("allstar")) top += 3;
            if (catAll.Contains("chef john")) top += 3;
            if (catAll.Contains("comfort food")) top += 1;
            if (HasAny(tl, TopTitleStrong)) top += 2;
            if (HasAny(tl, TopTitleSoft)) top += 1;
            r.Top = Math.Min(5, top);

            // 16. quality (used to pick the best when trimming near-duplicates)
            r.Quality = r.Bone
                      + (45 - r.TotalMin)
                      + r.Top * 10
                      + (6 - r.Diff) * 6
                      + (r.OnePot ? 8 : 0)
                      + (description != null && description.Length > 40 ? 8 : 0)
                      - (r.Fried ? 15 : 0);
            return r;
        }

        // ---------------------------------------------------------- json output
        public static string Esc(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n' || c == '\r' || c == '\t') sb.Append(' ');
                else if (c < 32) { }
                else if (c == '’' || c == '‘') sb.Append('\'');
                else if (c == '“' || c == '”') sb.Append('"');
                else if (c == '–' || c == '—') sb.Append('-');
                else if (c > 126) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Arr(IEnumerable<string> items)
        {
            StringBuilder sb = new StringBuilder("[");
            bool first = true;
            foreach (string s in items)
            {
                if (!first) sb.Append(",");
                sb.Append('"').Append(Esc(s)).Append('"');
                first = false;
            }
            return sb.Append("]").ToString();
        }

        public static string ToJson(Recipe r)
        {
            StringBuilder sb = new StringBuilder(1024);
            sb.Append("{\"i\":").Append(r.Id);
            sb.Append(",\"t\":\"").Append(Esc(r.Title)).Append('"');
            sb.Append(",\"d\":\"").Append(Esc(r.Description)).Append('"');
            sb.Append(",\"b\":\"").Append(Esc(r.Base)).Append('"');
            sb.Append(",\"bs\":").Append(Arr(r.Bases));
            sb.Append(",\"tc\":\"").Append(r.Temp).Append('"');
            sb.Append(",\"se\":\"").Append(r.Season).Append('"');
            sb.Append(",\"p\":").Append(r.PrepMin);
            sb.Append(",\"c\":").Append(r.CookMin);
            sb.Append(",\"tm\":").Append(r.TotalMin);
            sb.Append(",\"bn\":").Append(r.Bone);
            sb.Append(",\"bw\":").Append(Arr(r.BoneWhy));
            sb.Append(",\"pr\":\"").Append(Esc(r.Protein)).Append('"');
            sb.Append(",\"dt\":").Append(Arr(r.Diet));
            sb.Append(",\"cu\":\"").Append(Esc(r.Cuisine)).Append('"');
            sb.Append(",\"ef\":\"").Append(r.Effort).Append('"');
            sb.Append(",\"df\":").Append(r.Diff);
            sb.Append(",\"dl\":\"").Append(r.DiffLabel).Append('"');
            sb.Append(",\"tp\":").Append(r.Top);
            sb.Append(",\"op\":").Append(r.OnePot ? "1" : "0");
            sb.Append(",\"fr\":").Append(r.Fried ? "1" : "0");
            if (r.Servings > 0) sb.Append(",\"sv\":").Append(r.Servings);
            sb.Append(",\"ing\":").Append(Arr(r.Ingredients));
            sb.Append(",\"st\":").Append(Arr(r.Steps));
            sb.Append("}");
            return sb.ToString();
        }

        public static string NormTitle(string t)
        {
            string s = t.ToLowerInvariant();
            s = Regex.Replace(s, @"\b(chef john's|grandma's|grandmas|mom's|moms|easy|quick|best|simple|the|a|an|my|homemade|classic|super|super-easy|world's|award-winning|favorite|delicious|amazing|perfect|ultimate|and|with|for|two|one|i|ii|iii|iv)\b", " ");
            s = Regex.Replace(s, @"[^a-z0-9 ]", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }
    }
}
