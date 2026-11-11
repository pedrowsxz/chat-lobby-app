namespace Lobby.Web.Data;

// Hand-written pt-BR text for the demo. Bogus Lorem would be Latin and look fake.
public static class DemoContent
{
    public static readonly string[] StatusMessages =
    [
        "Lendo Dom Casmurro pela terceira vez", "No café da esquina ☕", "Em reunião até às 15h",
        "Ouvindo Novos Baianos", "De férias até segunda", "Trabalhando de casa hoje",
        "Procurando indicação de livro", "Correndo para o ônibus 🚌", "Estudando para a prova",
        "Fazendo pão caseiro", "Maratona de séries no fim de semana", "Disponível para conversar",
        "Treinando para a meia maratona", "Organizando a estante", "Cozinhando um risoto",
    ];

    public static readonly string[] ChatLines =
    [
        "Bom dia, pessoal!", "Alguém já terminou o capítulo 5?", "Eu terminei ontem à noite, que final!",
        "Sem spoilers, por favor 😅", "Ainda estou no capítulo 3.", "Esse livro começa devagar, mas melhora muito.",
        "Concordo, a segunda parte é bem melhor.", "Qual vai ser o próximo livro do clube?",
        "Eu voto em Vidas Secas.", "Que tal algo mais leve dessa vez?", "Pode ser uma ficção científica?",
        "Eu topo qualquer coisa do Machado.", "Já leram A Hora da Estrela?", "Li na escola, mas quero reler.",
        "A gente podia se encontrar no sábado.", "Sábado à tarde funciona para mim.", "Pode ser na livraria do centro?",
        "Lá tem um café ótimo.", "Eu levo bolo de cenoura!", "Combinado então 👍",
        "Alguém tem a edição com prefácio?", "Tenho, posso emprestar.", "Valeu! Te devolvo na semana que vem.",
        "Hoje o trabalho foi corrido.", "Nem me fale, foram três reuniões seguidas.", "Pelo menos amanhã é sexta.",
        "Vocês viram o show de ontem?", "Vi pela TV, a banda estava incrível.", "Queria ter ido ao vivo.",
        "Ano que vem a gente vai junto.", "Estou ouvindo o disco novo da Marisa Monte.", "Esse disco é lindo demais.",
        "Alguma indicação de podcast?", "Tem um ótimo sobre história do Brasil.", "Me manda o nome depois?",
        "Mando sim!", "Terminei o livro no ônibus hoje.", "E aí, gostou do final?", "Gostei, mas fiquei com dúvida.",
        "O narrador não é confiável, né?", "Exatamente, essa é a graça.", "Agora quero ver o filme.",
        "O filme é bom, mas o livro é melhor.", "Como sempre 😂", "Alguém quer dividir um táxi no sábado?",
        "Eu quero, saio do mesmo bairro.", "Perfeito, te chamo no sábado.", "Que horas vocês chegam?",
        "Lá pelas 15h.", "Vou me atrasar uns 20 minutos.", "Sem problema, a gente espera.",
        "Trouxe marcadores de página para todo mundo!", "Que fofo, obrigada!", "Adorei a ideia.",
        "Esse capítulo me fez chorar.", "Eu também, muito bonito.", "Não esperava aquela reviravolta.",
        "A autora escreve muito bem.", "Vou procurar outros livros dela.", "Ela tem um de contos também.",
        "Contos são ótimos para ler antes de dormir.", "Hoje vou dormir cedo, estou exausto.", "Boa noite, gente!",
        "Boa noite! Até amanhã.", "Alguém viu meu guarda-chuva? Esqueci na livraria.", "Acho que ficou com o atendente.",
        "Vou passar lá amanhã para pegar.", "Que calor hoje, hein?", "Dia perfeito para ler na praia.",
        "Ou no ar-condicionado 😄", "Vocês preferem livro físico ou digital?", "Físico, gosto do cheiro do papel.",
        "Digital, cabe na bolsa.", "Uso os dois, depende do dia.", "Comecei a ler em inglês para praticar.",
        "Que legal! Está sendo difícil?", "No começo sim, agora está fluindo.", "Vou tentar também.",
        "Semana que vem tem feira do livro!", "Vamos todos? Dá para garimpar edições baratas.",
    ];

    public const string BotGreeting =
        "Olá! Eu sou o Assistente Lobby, um bot de demonstração. Mande qualquer mensagem e eu respondo em tempo real.";

    public const string PairHello = "👋 Entrei pelo link de par. Agora é só conversar!";

    public static readonly string[] BotReplies =
    [
        "Oi! Eu sou o assistente do Lobby. Esta mensagem chegou em tempo real pelo SignalR ⚡",
        "Recebi! Repare que minha resposta apareceu sem você recarregar a página.",
        "Dica: abra o link de par em uma janela anônima para conversar com outro visitante ao vivo.",
        "Quer ver a paginação? Vá ao grupo \"Clube do livro (demo)\" e role para cima.",
        "Sou um bot, então apareço sempre offline. Só pessoas conectadas ficam com a bolinha verde.",
        "Sua mensagem foi salva no banco antes de ser enviada para os outros. Assim nada se perde.",
        "Você pode editar seu nome, recado e foto em \"Editar perfil\".",
        "Tudo que um visitante cria é apagado automaticamente depois de 24 horas.",
        "Legal! Pode mandar mais, estou ouvindo 🙂",
        "Se a conexão cair, o Lobby reconecta sozinho e busca as mensagens que faltaram.",
    ];

    public static readonly string[] GroupReplies =
    [
        "Bem-vindo ao clube! 📚", "Boa! Concordo com você.", "Haha, verdade.", "Ótima pergunta, também quero saber.",
        "Você já leu o livro deste mês?", "Sábado você vem com a gente?", "Anotado!", "Que bom ter você aqui.",
    ];
}
